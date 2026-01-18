using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfileToolProjectTests
{
    [Fact]
    public void ToolProjectDoesNotHardcodeVersion()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(repoRoot, "src", "ProfileTool", "ProfileTool.csproj");

        Assert.True(File.Exists(projectPath), $"Missing tool project at {projectPath}.");

        var document = XDocument.Load(projectPath);
        var rootNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
        var versionElements = document.Descendants(rootNamespace + "Version");

        Assert.Empty(versionElements);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Asynkron.Profiler.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from test output directory.");
    }
}
