namespace Asynkron.Profiler;

public sealed record ProfileRenderRequest(
    string ProfileName,
    string? Description,
    string? CallTreeRoot,
    string? FunctionFilter,
    string? ExceptionTypeFilter,
    bool IncludeRuntime,
    int CallTreeDepth,
    int CallTreeWidth,
    string? CallTreeRootMode,
    bool ShowSelfTimeTree,
    int CallTreeSiblingCutoffPercent,
    double HotThreshold,
    bool ShowTimeline,
    int TimelineWidth);
