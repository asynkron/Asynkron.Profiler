using System;
using System.Collections.Generic;

namespace Asynkron.Profiler;

public static class CallTreeFilters
{
    public static IReadOnlyList<CallTreeNode> GetVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        bool useSelfTime,
        int maxWidth,
        int siblingCutoffPercent,
        Func<string, bool> isRuntimeNoise)
    {
        return TreeVisibilityFilter.SelectTopChildren(
            TreeVisibilityFilter.EnumerateVisibleChildren(
                node.Children.Values,
                includeRuntime,
                child => isRuntimeNoise(child.Name),
                child => child.Children.Values),
            child => GetCallTreeTime(child, useSelfTime),
            maxWidth,
            siblingCutoffPercent);
    }

    private static double GetCallTreeTime(CallTreeNode node, bool useSelfTime)
    {
        return useSelfTime ? node.Self : node.Total;
    }
}
