using System;
using System.Collections.Generic;
using System.Linq;

namespace Asynkron.Profiler;

internal static class TreeVisibilityFilter
{
    public static IReadOnlyList<TNode> SelectTopChildren<TNode>(
        IEnumerable<TNode> candidates,
        Func<TNode, double> scoreSelector,
        int maxWidth,
        int siblingCutoffPercent)
    {
        var ordered = candidates
            .OrderByDescending(scoreSelector)
            .ToList();

        if (ordered.Count == 0)
        {
            return ordered;
        }

        if (siblingCutoffPercent <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var topScore = scoreSelector(ordered[0]);
        if (topScore <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var minScore = topScore * siblingCutoffPercent / 100d;
        return ordered
            .Where(child => scoreSelector(child) >= minScore)
            .Take(maxWidth)
            .ToList();
    }

    public static IEnumerable<TNode> EnumerateVisibleChildren<TNode>(
        IEnumerable<TNode> children,
        bool includeFilteredNodes,
        Func<TNode, bool> isFilteredNode,
        Func<TNode, IEnumerable<TNode>> childSelector)
    {
        foreach (var child in children)
        {
            if (includeFilteredNodes || !isFilteredNode(child))
            {
                yield return child;
                continue;
            }

            foreach (var grandChild in EnumerateVisibleChildren(
                         childSelector(child),
                         includeFilteredNodes,
                         isFilteredNode,
                         childSelector))
            {
                yield return grandChild;
            }
        }
    }
}
