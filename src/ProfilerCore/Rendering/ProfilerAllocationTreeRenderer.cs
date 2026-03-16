using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerAllocationTreeRenderer
{
    private readonly ProfilerCallTreeFormatter _formatter;
    private readonly ProfilerTreeFactory _treeFactory;

    public ProfilerAllocationTreeRenderer(
        ProfilerCallTreeFormatter formatter,
        ProfilerTreeFactory treeFactory)
    {
        _formatter = formatter;
        _treeFactory = treeFactory;
    }

    public Tree Build(
        AllocationCallTreeResult callTree,
        string? callTreeRoot,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        int callTreeSiblingCutoffPercent)
    {
        var rootLabel = callTree.TypeRoots.Count > 0
            ? NameFormatter.FormatTypeDisplayName(callTree.TypeRoots[0].Name)
            : "Allocations";
        var rootNode = callTree.TypeRoots.Count > 0
            ? callTree.TypeRoots[0]
            : new AllocationCallTreeNode(rootLabel);

        if (!string.IsNullOrWhiteSpace(callTreeRoot))
        {
            var matchingRoots = callTree.TypeRoots
                .Where(root => root.Name.Contains(callTreeRoot, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingRoots.Count > 0)
            {
                rootNode = matchingRoots[0];
            }
        }

        var tree = _treeFactory.Create(_formatter.FormatAllocationCallTreeLine(rootNode, rootNode.TotalBytes, isRoot: true, isLeaf: false));
        AddChildren(
            tree,
            rootNode,
            rootNode.TotalBytes,
            includeRuntime,
            depth: 1,
            callTreeDepth,
            callTreeWidth,
            callTreeSiblingCutoffPercent);
        return tree;
    }

    private void AddChildren(
        IHasTreeNodes parent,
        AllocationCallTreeNode node,
        long rootTotalBytes,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var children = GetVisibleChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var nextDepth = depth + 1;
            var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
            var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
                ? GetVisibleChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
                : new List<AllocationCallTreeNode>();
            var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

            var childNode = parent.AddNode(_formatter.FormatAllocationCallTreeLine(child, rootTotalBytes, isRoot: false, isLeaf));
            if (!isSpecialLeaf)
            {
                AddChildren(
                    childNode,
                    child,
                    rootTotalBytes,
                    includeRuntime,
                    nextDepth,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }
    }

    private static List<AllocationCallTreeNode> GetVisibleChildren(
        AllocationCallTreeNode node,
        bool includeRuntime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        var ordered = EnumerateVisibleChildren(node, includeRuntime)
            .OrderByDescending(child => child.TotalBytes)
            .ToList();

        if (ordered.Count == 0)
        {
            return ordered;
        }

        if (siblingCutoffPercent <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var topBytes = ordered[0].TotalBytes;
        if (topBytes <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var minBytes = topBytes * siblingCutoffPercent / 100d;
        return ordered
            .Where(child => child.TotalBytes >= minBytes)
            .Take(maxWidth)
            .ToList();
    }

    private static IEnumerable<AllocationCallTreeNode> EnumerateVisibleChildren(
        AllocationCallTreeNode node,
        bool includeRuntime)
    {
        foreach (var child in node.Children.Values)
        {
            if (includeRuntime || !IsRuntimeNoise(child.Name))
            {
                yield return child;
                continue;
            }

            foreach (var grandChild in EnumerateVisibleChildren(child, includeRuntime))
            {
                yield return grandChild;
            }
        }
    }
}
