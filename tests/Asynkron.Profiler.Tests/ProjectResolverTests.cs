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
        WithStandaloneProject(project =>
        {
            var resolver = CreateProjectResolver(project, targetFrameworks: string.Empty);
            var resolved = resolver.Resolve([project.ProjectPath], null);

            Assert.NotNull(resolved);
            Assert.Equal(new[] { "dotnet", project.GetTargetPath("net8.0") }, resolved!.Command);
            Assert.Equal("App", resolved.Label);
            Assert.Equal($"{Path.GetFullPath(project.ProjectPath)} (Release, net8.0)", resolved.Description);
        });
    }

    [Fact]
    public void RequiresTargetFrameworkWhenMultiple()
    {
        WithStandaloneProject(project =>
        {
            var resolver = CreateProjectResolver(project, "net8.0;net9.0");
            var resolved = resolver.Resolve([project.ProjectPath], null);

            Assert.Null(resolved);
        });
    }

    [Fact]
    public void ResolvesMultiTargetFrameworkWhenSpecified()
    {
        WithStandaloneProject(project =>
        {
            var resolver = CreateProjectResolver(project, "net8.0;net9.0");
            var resolved = resolver.Resolve([project.ProjectPath], "net9.0");

            Assert.NotNull(resolved);
            Assert.Equal(new[] { "dotnet", project.GetTargetPath("net9.0") }, resolved!.Command);
        });
    }

    private static readonly string[] SolutionProjects =
    [
        "Project(\"{00000000-0000-0000-0000-000000000000}\") = \"App\", \"src\\\\App\\\\App.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
        "EndProject",
        "Project(\"{00000000-0000-0000-0000-000000000000}\") = \"Lib\", \"src\\\\Lib\\\\Lib.csproj\", \"{22222222-2222-2222-2222-222222222222}\"",
        "EndProject"
    ];

    [Fact]
    public void ResolvesSolutionExecutableProject()
    {
        WithSolutionProjects(projects =>
        {
            var resolver = CreateSolutionResolver(projects);
            var resolved = resolver.Resolve([projects.SolutionPath], null);

            Assert.NotNull(resolved);
            Assert.Equal(new[] { "dotnet", projects.AppTargetPath }, resolved!.Command);
            Assert.Equal("App", resolved.Label);
        });
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

    private static void WithStandaloneProject(Action<TestProject> assert)
    {
        var root = CreateTempDirectory();
        try
        {
            var projectDir = Path.Combine(root, "App");
            Directory.CreateDirectory(projectDir);
            var projectPath = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");
            assert(new TestProject(root, projectDir, projectPath));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static ProjectResolver CreateProjectResolver(TestProject project, string targetFrameworks)
    {
        return new ProjectResolver((_, args, _, _) =>
        {
            var argList = args.ToList();
            if (argList.Contains("build"))
            {
                return (true, string.Empty, string.Empty);
            }

            if (argList.Contains("msbuild"))
            {
                var tfm = GetRequestedTargetFramework(argList) ?? "net8.0";
                var output = BuildMsbuildOutput(
                    $"TargetFramework={tfm}",
                    $"TargetFrameworks={targetFrameworks}",
                    "OutputType=Exe",
                    $"TargetPath={project.GetTargetPath(tfm)}");
                return (true, output, string.Empty);
            }

            return (false, string.Empty, "Unexpected command");
        });
    }

    private static void WithSolutionProjects(Action<SolutionProjectsFixture> assert)
    {
        var root = CreateTempDirectory();
        try
        {
            var appProjectPath = Path.Combine(root, "src", "App", "App.csproj");
            var libProjectPath = Path.Combine(root, "src", "Lib", "Lib.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(appProjectPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(libProjectPath)!);
            File.WriteAllText(appProjectPath, "<Project />");
            File.WriteAllText(libProjectPath, "<Project />");

            var solutionPath = Path.Combine(root, "App.sln");
            File.WriteAllText(solutionPath, string.Join(Environment.NewLine, SolutionProjects));
            assert(new SolutionProjectsFixture(
                root,
                solutionPath,
                appProjectPath,
                libProjectPath,
                Path.Combine(root, "src", "App", "bin", "Release", "net8.0", "App.dll"),
                Path.Combine(root, "src", "Lib", "bin", "Release", "net8.0", "Lib.dll")));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static ProjectResolver CreateSolutionResolver(SolutionProjectsFixture projects)
    {
        var appFullPath = Path.GetFullPath(projects.AppProjectPath);
        return new ProjectResolver((_, args, _, _) =>
        {
            var argList = args.ToList();
            if (argList.Contains("build"))
            {
                return (true, string.Empty, string.Empty);
            }

            if (argList.Contains("msbuild"))
            {
                var projectPath = argList.FirstOrDefault(arg =>
                    arg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
                if (projectPath == null)
                {
                    return (false, string.Empty, "Project path missing");
                }

                var fullProjectPath = Path.GetFullPath(projectPath);
                var isExecutable = fullProjectPath.Equals(appFullPath, StringComparison.OrdinalIgnoreCase);
                var output = BuildMsbuildOutput(
                    "TargetFramework=net8.0",
                    "TargetFrameworks=",
                    isExecutable ? "OutputType=Exe" : "OutputType=Library",
                    $"TargetPath={(isExecutable ? projects.AppTargetPath : projects.LibTargetPath)}");
                return (true, output, string.Empty);
            }

            return (false, string.Empty, "Unexpected command");
        });
    }

    private static string? GetRequestedTargetFramework(IEnumerable<string> args)
    {
        return args
            .FirstOrDefault(arg => arg.StartsWith("-property:TargetFramework=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
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

    private sealed record TestProject(string Root, string ProjectDirectory, string ProjectPath)
    {
        public string GetTargetPath(string targetFramework)
        {
            return Path.Combine(ProjectDirectory, "bin", "Release", targetFramework, "App.dll");
        }
    }

    private sealed record SolutionProjectsFixture(
        string Root,
        string SolutionPath,
        string AppProjectPath,
        string LibProjectPath,
        string AppTargetPath,
        string LibTargetPath);
}
