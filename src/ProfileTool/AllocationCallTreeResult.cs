using System.Collections.Generic;

namespace Asynkron.JsEngine.Tools.ProfileTool;

internal sealed record AllocationCallTreeResult(
    long TotalBytes,
    long TotalCount,
    IReadOnlyList<AllocationCallTreeNode> TypeRoots);
