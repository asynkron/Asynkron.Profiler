using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed record AllocationCallTreeResult(
    long TotalBytes,
    long TotalCount,
    IReadOnlyList<AllocationCallTreeNode> TypeRoots);
