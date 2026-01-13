using System;
using System.Collections.Generic;
using System.Linq;

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
        var ordered = EnumerateVisibleChildren(node, includeRuntime, isRuntimeNoise)
            .OrderByDescending(child => GetCallTreeTime(child, useSelfTime))
            .ToList();

        if (ordered.Count == 0)
        {
            return ordered;
        }

        if (siblingCutoffPercent <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var topTime = GetCallTreeTime(ordered[0], useSelfTime);
        if (topTime <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var minTime = topTime * siblingCutoffPercent / 100d;
        return ordered
            .Where(child => GetCallTreeTime(child, useSelfTime) >= minTime)
            .Take(maxWidth)
            .ToList();
    }

    private static IEnumerable<CallTreeNode> EnumerateVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        Func<string, bool> isRuntimeNoise)
    {
        foreach (var child in node.Children.Values)
        {
            if (includeRuntime || !isRuntimeNoise(child.Name))
            {
                yield return child;
                continue;
            }

            foreach (var grandChild in EnumerateVisibleChildren(child, includeRuntime, isRuntimeNoise))
            {
                yield return grandChild;
            }
        }
    }

    private static double GetCallTreeTime(CallTreeNode node, bool useSelfTime)
    {
        return useSelfTime ? node.Self : node.Total;
    }
}
