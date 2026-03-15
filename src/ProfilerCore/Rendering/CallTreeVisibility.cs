using System.Collections.Generic;

namespace Asynkron.Profiler;

internal static class CallTreeVisibility
{
    public static IReadOnlyList<CallTreeNode> GetVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        bool useSelfTime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        return CallTreeFilters.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent,
            CallTreeHelpers.IsRuntimeNoise);
    }

    public static bool HasVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        bool useSelfTime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        return GetVisibleChildren(node, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent).Count > 0;
    }
}
