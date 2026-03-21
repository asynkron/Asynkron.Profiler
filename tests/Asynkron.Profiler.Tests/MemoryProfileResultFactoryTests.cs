using System.Linq;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class MemoryProfileResultFactoryTests
{
    [Fact]
    public void Build_SortsEntriesAndCapsAtFifty()
    {
        var roots = Enumerable.Range(0, 55)
            .Select(index => new AllocationCallTreeNode($"Type{index}")
            {
                Count = index + 1,
                TotalBytes = index + 1
            })
            .ToArray();
        var callTree = new AllocationCallTreeResult(roots.Sum(root => root.TotalBytes), roots.Sum(root => root.Count), roots);

        var result = MemoryProfileResultFactory.Build(callTree);

        Assert.Equal("1.50 KB", result.TotalAllocated);
        Assert.Equal("1.50 KB", result.AllocationTotal);
        Assert.Same(callTree, result.AllocationCallTree);
        Assert.Equal(50, result.AllocationEntries.Count);
        Assert.Equal("Type54", result.AllocationEntries[0].Type);
        Assert.Equal("55 B", result.AllocationEntries[0].Total);
        Assert.Equal("Type5", result.AllocationEntries[^1].Type);
    }
}
