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
        var root = CreateTempDirectory();
        try
        {
            var projectDir = Path.Combine(root, "App");
            Directory.CreateDirectory(projectDir);
            var projectPath = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");

            var targetPath = Path.Combine(projectDir, "bin", "Release", "net8.0", "App.dll");

            (bool Success, string StdOut, string StdErr) RunProcess(
                string fileName,
                IEnumerable<string> args,
                string? workingDir,
                int timeoutMs)
            {
                var argList = args.ToList();
                if (argList.Contains("build"))
                {
                    return (true, string.Empty, string.Empty);
                }

                if (argList.Contains("msbuild"))
                {
                    var output = BuildMsbuildOutput(
                        "TargetFramework=net8.0",
                        "TargetFrameworks=",
                        "OutputType=Exe",
                        $"TargetPath={targetPath}");
                    return (true, output, string.Empty);
                }

                return (false, string.Empty, "Unexpected command");
            }

            var resolver = new ProjectResolver(RunProcess);
            var resolved = resolver.Resolve(new[] { projectPath }, null);

            Assert.NotNull(resolved);
            Assert.Equal(new[] { "dotnet", targetPath }, resolved!.Command);
            Assert.Equal("App", resolved.Label);
            Assert.Equal($"{Path.GetFullPath(projectPath)} (Release, net8.0)", resolved.Description);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void RequiresTargetFrameworkWhenMultiple()
    {
        var root = CreateTempDirectory();
        try
        {
            var projectDir = Path.Combine(root, "App");
            Directory.CreateDirectory(projectDir);
            var projectPath = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");

            var targetPath = Path.Combine(projectDir, "bin", "Release", "net8.0", "App.dll");

            (bool Success, string StdOut, string StdErr) RunProcess(
                string fileName,
                IEnumerable<string> args,
                string? workingDir,
                int timeoutMs)
            {
                var argList = args.ToList();
                if (argList.Contains("build"))
                {
                    return (true, string.Empty, string.Empty);
                }

                if (argList.Contains("msbuild"))
                {
                    var output = BuildMsbuildOutput(
                        "TargetFramework=net8.0",
                        "TargetFrameworks=net8.0;net9.0",
                        "OutputType=Exe",
                        $"TargetPath={targetPath}");
                    return (true, output, string.Empty);
                }

                return (false, string.Empty, "Unexpected command");
            }

            var resolver = new ProjectResolver(RunProcess);
            var resolved = resolver.Resolve(new[] { projectPath }, null);

            Assert.Null(resolved);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolvesMultiTargetFrameworkWhenSpecified()
    {
        var root = CreateTempDirectory();
        try
        {
            var projectDir = Path.Combine(root, "App");
            Directory.CreateDirectory(projectDir);
            var projectPath = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");

            (bool Success, string StdOut, string StdErr) RunProcess(
                string fileName,
                IEnumerable<string> args,
                string? workingDir,
                int timeoutMs)
            {
                var argList = args.ToList();
                if (argList.Contains("build"))
                {
                    return (true, string.Empty, string.Empty);
                }

                if (argList.Contains("msbuild"))
                {
                    var tfmArg = argList.FirstOrDefault(arg =>
                        arg.StartsWith("-property:TargetFramework=", StringComparison.OrdinalIgnoreCase));
                    var tfm = tfmArg?.Split('=', 2)[1] ?? "net8.0";
                    var targetPath = Path.Combine(projectDir, "bin", "Release", tfm, "App.dll");
                    var output = BuildMsbuildOutput(
                        $"TargetFramework={tfm}",
                        "TargetFrameworks=net8.0;net9.0",
                        "OutputType=Exe",
                        $"TargetPath={targetPath}");
                    return (true, output, string.Empty);
                }

                return (false, string.Empty, "Unexpected command");
            }

            var resolver = new ProjectResolver(RunProcess);
            var resolved = resolver.Resolve(new[] { projectPath }, "net9.0");

            Assert.NotNull(resolved);
            Assert.Equal(new[] { "dotnet", Path.Combine(projectDir, "bin", "Release", "net9.0", "App.dll") }, resolved!.Command);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolvesSolutionExecutableProject()
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
            File.WriteAllText(solutionPath, string.Join(Environment.NewLine, new[]
            {
                "Project(\"{00000000-0000-0000-0000-000000000000}\") = \"App\", \"src\\\\App\\\\App.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
                "EndProject",
                "Project(\"{00000000-0000-0000-0000-000000000000}\") = \"Lib\", \"src\\\\Lib\\\\Lib.csproj\", \"{22222222-2222-2222-2222-222222222222}\"",
                "EndProject"
            }));

            var appTargetPath = Path.Combine(root, "src", "App", "bin", "Release", "net8.0", "App.dll");
            var libTargetPath = Path.Combine(root, "src", "Lib", "bin", "Release", "net8.0", "Lib.dll");
            var appFullPath = Path.GetFullPath(appProjectPath);
            var libFullPath = Path.GetFullPath(libProjectPath);

            (bool Success, string StdOut, string StdErr) RunProcess(
                string fileName,
                IEnumerable<string> args,
                string? workingDir,
                int timeoutMs)
            {
                var argList = args.ToList();
                if (argList.Contains("build"))
                {
                    return (true, string.Empty, string.Empty);
                }

                if (argList.Contains("msbuild"))
                {
                    var project = argList.FirstOrDefault(arg =>
                        arg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
                    if (project == null)
                    {
                        return (false, string.Empty, "Project path missing");
                    }

                    var fullProject = Path.GetFullPath(project);
                    var outputType = fullProject.Equals(appFullPath, StringComparison.OrdinalIgnoreCase)
                        ? "Exe"
                        : "Library";
                    var targetPath = fullProject.Equals(appFullPath, StringComparison.OrdinalIgnoreCase)
                        ? appTargetPath
                        : libTargetPath;
                    var output = BuildMsbuildOutput(
                        "TargetFramework=net8.0",
                        "TargetFrameworks=",
                        $"OutputType={outputType}",
                        $"TargetPath={targetPath}");
                    return (true, output, string.Empty);
                }

                return (false, string.Empty, "Unexpected command");
            }

            var resolver = new ProjectResolver(RunProcess);
            var resolved = resolver.Resolve(new[] { solutionPath }, null);

            Assert.NotNull(resolved);
            Assert.Equal(new[] { "dotnet", appTargetPath }, resolved!.Command);
            Assert.Equal("App", resolved.Label);
        }
        finally
        {
            SafeDeleteDirectory(root);
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
}
