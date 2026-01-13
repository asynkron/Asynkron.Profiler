using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed record AllocationCallTreeResult(
    long TotalBytes,
    long TotalCount,
    IReadOnlyList<AllocationCallTreeNode> TypeRoots);
