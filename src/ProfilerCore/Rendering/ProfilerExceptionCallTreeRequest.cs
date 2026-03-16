namespace Asynkron.Profiler;

internal sealed record ProfilerExceptionCallTreeRequest(
    CallTreeNode CallTreeRoot,
    long TotalCount,
    string Title,
    string? RootLabelOverride,
    ProfilerTreeRootSelectionOptions Options);
