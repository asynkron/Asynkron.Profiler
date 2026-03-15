using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class CallTreeHelpersTests
{
    [Fact]
    public void SelectRootMatchSkipsRuntimeNodesWhenRuntimeFramesAreHidden()
    {
        var runtime = new CallTreeNode(1, "Thread") { Total = 100 };
        var app = new CallTreeNode(2, "MyApp.Work") { Total = 10 };
        var matches = new[]
        {
            new CallTreeMatch(runtime, Depth: 0, Order: 0),
            new CallTreeMatch(app, Depth: 1, Order: 1)
        };

        var selected = CallTreeHelpers.SelectRootMatch(matches, includeRuntime: false, rootMode: "first");

        Assert.Same(app, selected);
    }

    [Fact]
    public void CollectHotMethodsBuildsStableJitFilters()
    {
        var root = new CallTreeNode(-1, "root") { Calls = 10, Total = 100 };
        var hot = new CallTreeNode(1, "My.Namespace.Worker.Run(System.Int32)") { Calls = 5, Self = 60, Total = 70 };
        var unmanaged = new CallTreeNode(2, "UNMANAGED_CODE_TIME") { Calls = 5, Self = 60, Total = 70 };

        root.Children[1] = hot;
        root.Children[2] = unmanaged;

        var methods = CallTreeHelpers.CollectHotMethods(
            root,
            totalTime: 100,
            totalSamples: 10,
            includeRuntime: false,
            hotThreshold: 0.1);

        var method = Assert.Single(methods);
        Assert.Equal("My.Namespace.Worker:Run", method.Filter);
        Assert.Equal("Worker.Run", method.DisplayName);
        Assert.True(method.Hotness >= 0.1);
    }
}
