using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProjectResolverTests
{
    [Fact]
    public void ResolvesSingleTargetFrameworkProject()
    {
        using var scenario = new ProjectResolverTestScenario();
        var projectPath = scenario.CreateProject("App/App.csproj", outputType: "Exe");

        var resolved = scenario.CreateResolver().Resolve([projectPath], null);

        Assert.NotNull(resolved);
        Assert.Equal(new[] { "dotnet", ProjectResolverTestScenario.GetTargetPath(projectPath, "net8.0") }, resolved!.Command);
        Assert.Equal("App", resolved.Label);
        Assert.Equal($"{Path.GetFullPath(projectPath)} (Release, net8.0)", resolved.Description);
    }

    [Fact]
    public void RequiresTargetFrameworkWhenMultiple()
    {
        using var scenario = new ProjectResolverTestScenario();
        var projectPath = scenario.CreateProject(
            "App/App.csproj",
            outputType: "Exe",
            targetFrameworks: "net8.0;net9.0");

        var resolved = scenario.CreateResolver().Resolve([projectPath], null);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolvesMultiTargetFrameworkWhenSpecified()
    {
        using var scenario = new ProjectResolverTestScenario();
        var projectPath = scenario.CreateProject(
            "App/App.csproj",
            outputType: "Exe",
            targetFrameworks: "net8.0;net9.0");

        var resolved = scenario.CreateResolver().Resolve([projectPath], "net9.0");

        Assert.NotNull(resolved);
        Assert.Equal(new[] { "dotnet", ProjectResolverTestScenario.GetTargetPath(projectPath, "net9.0") }, resolved!.Command);
    }

    [Fact]
    public void ResolvesSolutionExecutableProject()
    {
        using var scenario = new ProjectResolverTestScenario();
        var appProjectPath = scenario.CreateProject("src/App/App.csproj", outputType: "Exe");
        var libProjectPath = scenario.CreateProject("src/Lib/Lib.csproj", outputType: "Library");
        var solutionPath = scenario.CreateSolution(
            ("App", appProjectPath, "{11111111-1111-1111-1111-111111111111}"),
            ("Lib", libProjectPath, "{22222222-2222-2222-2222-222222222222}"));

        var resolved = scenario.CreateResolver().Resolve([solutionPath], null);

        Assert.NotNull(resolved);
        Assert.Equal(new[] { "dotnet", ProjectResolverTestScenario.GetTargetPath(appProjectPath, "net8.0") }, resolved!.Command);
        Assert.Equal("App", resolved.Label);
    }

    private sealed class ProjectResolverTestScenario : IDisposable
    {
        private readonly Dictionary<string, ProjectMetadata> _projects =
            new(StringComparer.OrdinalIgnoreCase);

        public ProjectResolverTestScenario()
        {
            Root = CreateTempDirectory();
        }

        public string Root { get; }

        public string CreateProject(
            string relativePath,
            string outputType,
            string targetFramework = "net8.0",
            string targetFrameworks = "")
        {
            var projectPath = ToFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(projectPath, "<Project />");
            _projects[Path.GetFullPath(projectPath)] = new ProjectMetadata(
                targetFramework,
                targetFrameworks,
                outputType);
            return projectPath;
        }

        public string CreateSolution(params (string Name, string ProjectPath, string Id)[] projects)
        {
            var lines = new List<string>();
            foreach (var (name, projectPath, id) in projects)
            {
                var relativePath = Path.GetRelativePath(Root, projectPath)
                    .Replace("\\", "\\\\", StringComparison.Ordinal);
                lines.Add(
                    $"Project(\"{{00000000-0000-0000-0000-000000000000}}\") = \"{name}\", \"{relativePath}\", \"{id}\"");
                lines.Add("EndProject");
            }

            var solutionPath = Path.Combine(Root, "App.sln");
            File.WriteAllText(solutionPath, string.Join(Environment.NewLine, lines));
            return solutionPath;
        }

        public static string GetTargetPath(string projectPath, string targetFramework)
        {
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var assemblyName = Path.GetFileNameWithoutExtension(projectPath);
            return Path.Combine(projectDirectory, "bin", "Release", targetFramework, $"{assemblyName}.dll");
        }

        public ProjectResolver CreateResolver()
        {
            return new ProjectResolver(RunProcess);
        }

        public void Dispose()
        {
            SafeDeleteDirectory(Root);
        }

        private (bool Success, string StdOut, string StdErr) RunProcess(
            string fileName,
            IEnumerable<string> args,
            string? workingDir,
            int timeoutMs)
        {
            _ = fileName;
            _ = workingDir;
            _ = timeoutMs;

            var argList = args.ToList();
            if (argList.Contains("build"))
            {
                return (true, string.Empty, string.Empty);
            }

            if (!argList.Contains("msbuild"))
            {
                return (false, string.Empty, "Unexpected command");
            }

            var projectPath = argList.FirstOrDefault(arg =>
                arg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (projectPath == null)
            {
                return (false, string.Empty, "Project path missing");
            }

            var fullProjectPath = Path.GetFullPath(projectPath);
            if (!_projects.TryGetValue(fullProjectPath, out var metadata))
            {
                return (false, string.Empty, "Project path missing");
            }

            var targetFrameworkArg = argList.FirstOrDefault(arg =>
                arg.StartsWith("-property:TargetFramework=", StringComparison.OrdinalIgnoreCase));
            var targetFramework = targetFrameworkArg?.Split('=', 2)[1] ?? metadata.TargetFramework;
            var output = BuildMsbuildOutput(
                $"TargetFramework={targetFramework}",
                $"TargetFrameworks={metadata.TargetFrameworks}",
                $"OutputType={metadata.OutputType}",
                $"TargetPath={GetTargetPath(fullProjectPath, targetFramework)}");
            return (true, output, string.Empty);
        }

        private string ToFullPath(string relativePath)
        {
            var normalized = relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Root, normalized);
        }
    }

    private static string BuildMsbuildOutput(params string[] lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AsynkronProfilerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private readonly record struct ProjectMetadata(
        string TargetFramework,
        string TargetFrameworks,
        string OutputType);
}
