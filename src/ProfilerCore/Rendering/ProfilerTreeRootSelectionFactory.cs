namespace Asynkron.Profiler;

internal static class ProfilerTreeRootSelectionFactory
{
    public static ProfilerTreeRootSelectionOptions Build(ProfileRenderRequest request)
    {
        return new ProfilerTreeRootSelectionOptions(
            NormalizeRootFilter(request.CallTreeRoot),
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent);
    }

    public static string? NormalizeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }
}
