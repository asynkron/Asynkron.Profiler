using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProjectResolver
{
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;

    public ProjectResolver(Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess)
    {
        _runProcess = runProcess;
    }

    public ResolvedCommand? Resolve(string[] command, string? targetFramework)
    {
        if (command.Length == 1)
        {
            var path = command[0];
            if (File.Exists(path))
            {
                var extension = Path.GetExtension(path);
                if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveProjectCommand(path, targetFramework, buildProject: true);
                }

                if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveSolutionCommand(path, targetFramework);
                }
            }
        }

        var label = BuildCommandLabel(command);
        var description = BuildCommandDescription(command);
        return new ResolvedCommand(command, label, description);
    }

    private ResolvedCommand? ResolveSolutionCommand(string solutionPath, string? targetFramework)
    {
        var fullSolutionPath = Path.GetFullPath(solutionPath);
        var solutionDir = Path.GetDirectoryName(fullSolutionPath);
        if (solutionDir == null)
        {
            AnsiConsole.MarkupLine($"[red]Invalid solution path:[/] {Markup.Escape(solutionPath)}");
            return null;
        }

        var buildArgs = new[] { "build", "-c", "Release", fullSolutionPath };
        var (buildSuccess, _, buildErr) = _runProcess("dotnet", buildArgs, null, 600000);
        if (!buildSuccess)
        {
            AnsiConsole.MarkupLine($"[red]Build failed:[/] {Markup.Escape(buildErr)}");
            return null;
        }

        var projectPaths = GetSolutionProjects(fullSolutionPath)
            .Select(path => Path.GetFullPath(Path.Combine(solutionDir, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projectPaths.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No projects found in solution:[/] {Markup.Escape(solutionPath)}");
            return null;
        }

        var exeProjects = new List<string>();
        foreach (var projectPath in projectPaths)
        {
            var outputType = GetProjectOutputType(projectPath, targetFramework);
            if (outputType is "Exe" or "WinExe")
            {
                exeProjects.Add(projectPath);
            }
        }

        if (exeProjects.Count == 1)
        {
            return ResolveProjectCommand(exeProjects[0], targetFramework, buildProject: false);
        }

        if (exeProjects.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No executable projects found in solution.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Multiple executable projects found in solution.[/]");
        }

        foreach (var project in exeProjects)
        {
            AnsiConsole.MarkupLine($"[dim]- {Markup.Escape(project)}[/]");
        }

        AnsiConsole.MarkupLine("[dim]Pass a .csproj path directly to profile a specific project.[/]");
        return null;
    }

    private string? GetProjectOutputType(string projectPath, string? targetFramework)
    {
        var props = GetMsbuildProperties(projectPath, targetFramework);
        if (props == null)
        {
            return null;
        }

        return props.TryGetValue("OutputType", out var outputType)
            ? outputType
            : null;
    }

    private ResolvedCommand? ResolveProjectCommand(string projectPath, string? targetFramework, bool buildProject)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath))
        {
            AnsiConsole.MarkupLine($"[red]Project not found:[/] {Markup.Escape(projectPath)}");
            return null;
        }

        if (buildProject)
        {
            var buildArgs = new[] { "build", "-c", "Release", fullProjectPath };
            var (buildSuccess, _, buildErr) = _runProcess("dotnet", buildArgs, null, 600000);
            if (!buildSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Build failed:[/] {Markup.Escape(buildErr)}");
                return null;
            }
        }

        var initialProps = GetMsbuildProperties(fullProjectPath, targetFramework);
        if (initialProps == null)
        {
            return null;
        }

        var outputType = initialProps.TryGetValue("OutputType", out var outputTypeValue)
            ? outputTypeValue
            : string.Empty;

        if (outputType is not ("Exe" or "WinExe"))
        {
            AnsiConsole.MarkupLine($"[red]Project is not executable:[/] {Markup.Escape(fullProjectPath)}");
            return null;
        }

        var tfm = ResolveTargetFramework(initialProps, targetFramework, fullProjectPath);
        if (string.IsNullOrWhiteSpace(tfm))
        {
            return null;
        }

        var finalProps = GetMsbuildProperties(fullProjectPath, tfm);
        if (finalProps == null)
        {
            return null;
        }

        if (!finalProps.TryGetValue("TargetPath", out var targetPath) || string.IsNullOrWhiteSpace(targetPath))
        {
            AnsiConsole.MarkupLine($"[red]Could not determine output path for:[/] {Markup.Escape(fullProjectPath)}");
            return null;
        }

        var command = ResolveTargetCommand(targetPath);
        var label = Path.GetFileNameWithoutExtension(fullProjectPath);
        var description = $"{fullProjectPath} (Release, {tfm})";
        return new ResolvedCommand(command, label, description);
    }

    private Dictionary<string, string>? GetMsbuildProperties(string projectPath, string? targetFramework)
    {
        var args = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:OutputType",
            "-getProperty:TargetPath",
            "-property:Configuration=Release"
        };

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            args.Add($"-property:TargetFramework={targetFramework}");
        }

        var (success, stdout, stderr) = _runProcess("dotnet", args, null, 600000);
        if (!success)
        {
            AnsiConsole.MarkupLine($"[red]Failed to query project metadata:[/] {Markup.Escape(stderr)}");
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = stdout.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("Properties", out var props))
                {
                    foreach (var property in props.EnumerateObject())
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(property.Name))
                        {
                            result[property.Name] = value ?? string.Empty;
                        }
                    }

                    return result;
                }
            }
            catch
            {
                // Fall back to line parsing if JSON parsing fails.
            }
        }

        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private string? ResolveTargetFramework(
        Dictionary<string, string> props,
        string? targetFramework,
        string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            return targetFramework;
        }

        if (props.TryGetValue("TargetFrameworks", out var frameworksValue) &&
            !string.IsNullOrWhiteSpace(frameworksValue))
        {
            var frameworks = frameworksValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (frameworks.Count == 1)
            {
                return frameworks[0];
            }

            AnsiConsole.MarkupLine($"[red]Multiple target frameworks found for:[/] {Markup.Escape(projectPath)}");
            AnsiConsole.MarkupLine($"[dim]Use --tfm <tfm> to select one: {Markup.Escape(frameworksValue)}[/]");
            return null;
        }

        return props.TryGetValue("TargetFramework", out var singleFramework)
            ? singleFramework
            : null;
    }

    private string[] ResolveTargetCommand(string targetPath)
    {
        if (targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { "dotnet", targetPath };
        }

        return new[] { targetPath };
    }

    private IEnumerable<string> GetSolutionProjects(string solutionPath)
    {
        var projectPattern = new Regex("Project\\([^)]*\\)\\s*=\\s*\"[^\"]+\",\\s*\"([^\"]+\\.csproj)\"", RegexOptions.IgnoreCase);
        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = projectPattern.Match(line);
            if (match.Success)
            {
                yield return NormalizeSolutionPath(match.Groups[1].Value);
            }
        }
    }

    private static string NormalizeSolutionPath(string solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return solutionPath;
        }

        return solutionPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string BuildCommandLabel(string[] command)
    {
        if (command.Length == 0)
        {
            return "command";
        }

        var name = Path.GetFileNameWithoutExtension(command[0]);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "command";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private static string BuildCommandDescription(string[] command)
    {
        return command.Length == 0 ? string.Empty : string.Join(' ', command);
    }
}

internal sealed record ResolvedCommand(string[] Command, string Label, string Description);
