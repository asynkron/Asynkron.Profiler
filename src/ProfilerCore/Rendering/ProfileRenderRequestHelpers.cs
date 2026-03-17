using System;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal static class ProfileRenderRequestHelpers
{
    public static string? ResolveCallTreeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }

    public static bool MatchesFunctionFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               FormatFunctionDisplayName(name).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public static ProfilerTreeRootSelectionOptions BuildTreeRootSelectionOptions(ProfileRenderRequest request)
    {
        return new ProfilerTreeRootSelectionOptions(
            ResolveCallTreeRootFilter(request.CallTreeRoot),
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent);
    }
}
