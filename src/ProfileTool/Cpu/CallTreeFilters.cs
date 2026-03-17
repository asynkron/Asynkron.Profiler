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
            child => CallTreeHelpers.GetCallTreeTime(child, useSelfTime),
            maxWidth,
            siblingCutoffPercent);
    }

    public static bool HasVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        bool useSelfTime,
        int maxWidth,
        int siblingCutoffPercent,
        Func<string, bool> isRuntimeNoise)
    {
        return GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent,
            isRuntimeNoise).Count > 0;
    }
}
