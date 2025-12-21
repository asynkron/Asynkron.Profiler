using System;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class CallTreeFiltersTests
{
    [Fact]
    public void SkipsRuntimeNodesWhenFiltering()
    {
        var root = new CallTreeNode(-1, "root");
        var runtime = new CallTreeNode(1, "Thread") { Total = 10 };
        var app = new CallTreeNode(2, "MyApp.Run") { Total = 8 };

        runtime.Children[2] = app;
        root.Children[1] = runtime;

        var visible = CallTreeFilters.GetVisibleChildren(
            root,
            includeRuntime: false,
            useSelfTime: false,
            maxWidth: 10,
            siblingCutoffPercent: 0,
            isRuntimeNoise: name => name.Contains("Thread", StringComparison.Ordinal));

        Assert.Single(visible);
        Assert.Equal("MyApp.Run", visible[0].Name);
    }

    [Fact]
    public void AppliesSiblingCutoff()
    {
        var root = new CallTreeNode(-1, "root");
        var big = new CallTreeNode(1, "Big") { Total = 100 };
        var small = new CallTreeNode(2, "Small") { Total = 4 };

        root.Children[1] = big;
        root.Children[2] = small;

        var visible = CallTreeFilters.GetVisibleChildren(
            root,
            includeRuntime: true,
            useSelfTime: false,
            maxWidth: 10,
            siblingCutoffPercent: 5,
            isRuntimeNoise: _ => false);

        Assert.Single(visible);
        Assert.Equal("Big", visible[0].Name);
    }

    [Fact]
    public void HonorsMaxWidth()
    {
        var root = new CallTreeNode(-1, "root");
        root.Children[1] = new CallTreeNode(1, "One") { Total = 10 };
        root.Children[2] = new CallTreeNode(2, "Two") { Total = 9 };
        root.Children[3] = new CallTreeNode(3, "Three") { Total = 8 };

        var visible = CallTreeFilters.GetVisibleChildren(
            root,
            includeRuntime: true,
            useSelfTime: false,
            maxWidth: 2,
            siblingCutoffPercent: 0,
            isRuntimeNoise: _ => false);

        Assert.Equal(2, visible.Count);
        Assert.Equal("One", visible[0].Name);
        Assert.Equal("Two", visible[1].Name);
    }
}
