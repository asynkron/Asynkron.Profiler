using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Asynkron.Profiler;

internal sealed class ProjectResolver
{
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly MsbuildProjectMetadataReader _metadataReader;

    public ProjectResolver(Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess)
    {
        _runProcess = runProcess;
        _metadataReader = new MsbuildProjectMetadataReader(runProcess);
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
            ProjectResolverReporter.WriteInvalidSolutionPath(solutionPath);
            return null;
        }

        if (!TryBuildTarget(fullSolutionPath))
        {
            return null;
        }

        var projectPaths = SolutionProjectLocator.GetProjectPaths(fullSolutionPath);
        if (projectPaths.Count == 0)
        {
            ProjectResolverReporter.WriteNoProjectsInSolution(solutionPath);
            return null;
        }

        var exeProjects = new List<string>();
        foreach (var projectPath in projectPaths)
        {
            var metadata = _metadataReader.Read(projectPath, targetFramework);
            if (metadata?.OutputType is "Exe" or "WinExe")
            {
                exeProjects.Add(projectPath);
            }
        }

        if (exeProjects.Count == 1)
        {
            return ResolveProjectCommand(exeProjects[0], targetFramework, buildProject: false);
        }

        ProjectResolverReporter.WriteExecutableProjectsNotResolved(exeProjects);
        return null;
    }

    private ResolvedCommand? ResolveProjectCommand(string projectPath, string? targetFramework, bool buildProject)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath))
        {
            ProjectResolverReporter.WriteProjectNotFound(projectPath);
            return null;
        }

        if (buildProject)
        {
            if (!TryBuildTarget(fullProjectPath))
            {
                return null;
            }
        }

        var initialMetadata = _metadataReader.Read(fullProjectPath, targetFramework);
        if (initialMetadata == null)
        {
            return null;
        }

        if (initialMetadata.OutputType is not ("Exe" or "WinExe"))
        {
            ProjectResolverReporter.WriteProjectNotExecutable(fullProjectPath);
            return null;
        }

        var tfm = ProjectLaunchResolver.ResolveTargetFramework(initialMetadata, targetFramework, fullProjectPath);
        if (string.IsNullOrWhiteSpace(tfm))
        {
            return null;
        }

        var finalMetadata = _metadataReader.Read(fullProjectPath, tfm);
        if (finalMetadata == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(finalMetadata.TargetPath))
        {
            ProjectResolverReporter.WriteTargetPathNotFound(fullProjectPath);
            return null;
        }

        var command = ProjectLaunchResolver.ResolveTargetCommand(finalMetadata.TargetPath);
        var label = Path.GetFileNameWithoutExtension(fullProjectPath);
        var description = $"{fullProjectPath} (Release, {tfm})";
        return new ResolvedCommand(command, label, description);
    }

    private static string BuildCommandLabel(string[] command)
    {
        if (command.Length == 0)
        {
            return "command";
        }

        return FileLabelSanitizer.Sanitize(Path.GetFileNameWithoutExtension(command[0]), "command");
    }

    private static string BuildCommandDescription(string[] command)
    {
        return command.Length == 0 ? string.Empty : string.Join(' ', command);
    }

    private bool TryBuildTarget(string targetPath)
    {
        var buildArgs = new[] { "build", "-c", "Release", targetPath };
        var (buildSuccess, _, buildErr) = _runProcess("dotnet", buildArgs, null, 600000);
        if (buildSuccess)
        {
            return true;
        }

        ProjectResolverReporter.WriteBuildFailed(buildErr);
        return false;
    }
}

internal sealed record ResolvedCommand(string[] Command, string Label, string Description);
