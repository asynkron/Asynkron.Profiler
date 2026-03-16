namespace Asynkron.Profiler;

internal sealed record ProfilerTreeRootSelectionOptions(
    string? RootFilter,
    bool IncludeRuntime,
    int MaxDepth,
    int MaxWidth,
    string? RootMode,
    int SiblingCutoffPercent)
{
    public CallTreeTraversalSettings ToTraversalSettings()
    {
        return CallTreeTraversalSettings.Create(MaxDepth, MaxWidth, SiblingCutoffPercent);
    }
}
