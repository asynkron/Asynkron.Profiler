namespace Asynkron.Profiler;

internal sealed record ProfilerCallTreeRenderContext(
    double RootTotal,
    double TotalSamples,
    bool UseSelfTime,
    bool IncludeRuntime,
    CallTreeTraversalSettings Traversal,
    string TimeUnitLabel,
    string CountSuffix,
    int AllocationTypeLimit,
    int ExceptionTypeLimit,
    double HotThreshold,
    bool HighlightHotspots);
