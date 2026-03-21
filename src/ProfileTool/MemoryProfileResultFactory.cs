using System.Linq;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal static class MemoryProfileResultFactory
{
    public static MemoryProfileResult Build(AllocationCallTreeResult callTree)
    {
        var allocationEntries = callTree.TypeRoots
            .OrderByDescending(root => root.TotalBytes)
            .Take(50)
            .Select(root => new AllocationEntry(root.Name, root.Count, FormatBytes(root.TotalBytes)))
            .ToList();

        var totalAllocated = FormatBytes(callTree.TotalBytes);

        return new MemoryProfileResult(
            null,
            null,
            null,
            totalAllocated,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            totalAllocated,
            allocationEntries,
            callTree,
            null,
            null);
    }
}
