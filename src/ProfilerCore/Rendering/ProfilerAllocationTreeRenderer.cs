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

    public Tree Build(ProfilerAllocationCallTreeRequest request)
    {
        var callTree = request.CallTree;
        var rootLabel = callTree.TypeRoots.Count > 0
            ? NameFormatter.FormatTypeDisplayName(callTree.TypeRoots[0].Name)
            : "Allocations";
        var rootNode = callTree.TypeRoots.Count > 0
            ? callTree.TypeRoots[0]
            : new AllocationCallTreeNode(rootLabel);

        if (!string.IsNullOrWhiteSpace(request.RootFilter))
        {
            var matchingRoot = callTree.TypeRoots.FirstOrDefault(
                root => root.Name.Contains(request.RootFilter, StringComparison.OrdinalIgnoreCase));
            if (matchingRoot != null)
            {
                rootNode = matchingRoot;
            }
        }

        var tree = _treeFactory.Create(_formatter.FormatAllocationCallTreeLine(rootNode, rootNode.TotalBytes, isRoot: true, isLeaf: false));
        AddChildren(
            tree,
            rootNode,
            rootNode.TotalBytes,
            request.IncludeRuntime,
            depth: 1,
            request.Traversal);
        return tree;
    }

    private void AddChildren(
        IHasTreeNodes parent,
        AllocationCallTreeNode node,
        long rootTotalBytes,
        bool includeRuntime,
        int depth,
        CallTreeTraversalSettings traversal)
    {
        if (depth > traversal.MaxDepth)
        {
            return;
        }

        var children = GetVisibleChildren(node, includeRuntime, traversal);
        foreach (var child in children)
        {
            var nextDepth = depth + 1;
            var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
            var childChildren = !isSpecialLeaf && nextDepth <= traversal.MaxDepth
                ? GetVisibleChildren(child, includeRuntime, traversal)
                : new List<AllocationCallTreeNode>();
            var isLeaf = isSpecialLeaf || nextDepth > traversal.MaxDepth || childChildren.Count == 0;

            var childNode = parent.AddNode(_formatter.FormatAllocationCallTreeLine(child, rootTotalBytes, isRoot: false, isLeaf));
            if (!isSpecialLeaf)
            {
                AddChildren(
                    childNode,
                    child,
                    rootTotalBytes,
                    includeRuntime,
                    nextDepth,
                    traversal);
            }
        }
    }

    private static List<AllocationCallTreeNode> GetVisibleChildren(
        AllocationCallTreeNode node,
        bool includeRuntime,
        CallTreeTraversalSettings traversal)
    {
        return TreeVisibilityFilter.SelectTopChildren(
            TreeVisibilityFilter.EnumerateVisibleChildren(
                node.Children.Values,
                includeRuntime,
                child => IsRuntimeNoise(child.Name),
                child => child.Children.Values),
            child => child.TotalBytes,
            traversal.MaxWidth,
            traversal.SiblingCutoffPercent).ToList();
    }
}
