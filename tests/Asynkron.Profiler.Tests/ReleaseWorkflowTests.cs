using System;
using System.IO;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void PackWorkflowUsesTagVersion()
    {
        var workflowContents = LoadPackWorkflow();

        Assert.Contains("tags:", workflowContents, StringComparison.Ordinal);
        Assert.Contains("v*", workflowContents, StringComparison.Ordinal);
        Assert.Contains("GITHUB_REF_NAME#v", workflowContents, StringComparison.Ordinal);
        Assert.Contains("-p:Version", workflowContents, StringComparison.Ordinal);
    }

    [Fact]
    public void PackWorkflowValidatesPackageVersions()
    {
        var workflowContents = LoadPackWorkflow();

        Assert.Contains("Validate package versions", workflowContents, StringComparison.Ordinal);
        Assert.Contains("./nupkg/*.nupkg", workflowContents, StringComparison.Ordinal);
    }

    private static string LoadPackWorkflow()
    {
        var repoRoot = FindRepoRoot();
        var workflowPath = Path.Combine(repoRoot, ".github", "workflows", "pack.yml");

        Assert.True(File.Exists(workflowPath), $"Missing pack workflow at {workflowPath}.");

        return File.ReadAllText(workflowPath);
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
