namespace Asynkron.Profiler;

internal sealed record ProfilerAllocationCallTreeRequest(
    AllocationCallTreeResult CallTree,
    string? RootFilter,
    bool IncludeRuntime,
    CallTreeTraversalSettings Traversal);
