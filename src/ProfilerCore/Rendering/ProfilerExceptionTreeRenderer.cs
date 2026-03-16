using System;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerExceptionTreeRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeFormatter _formatter;
    private readonly ProfilerTreeFactory _treeFactory;
    private readonly ProfilerCallTreeRootResolver _rootResolver;

    public ProfilerExceptionTreeRenderer(
        Theme theme,
        ProfilerCallTreeFormatter formatter,
        ProfilerTreeFactory treeFactory)
    {
        _theme = theme;
        _formatter = formatter;
        _treeFactory = treeFactory;
        _rootResolver = new ProfilerCallTreeRootResolver(theme);
    }

    public Rows Build(
        CallTreeNode callTreeRoot,
        long totalCount,
        string title,
        string? rootLabelOverride,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent)
    {
        var traversal = CallTreeTraversalSettings.Create(maxDepth, maxWidth, siblingCutoffPercent);
        var rootSelection = _rootResolver.Resolve(
            callTreeRoot,
            totalCount,
            title,
            rootFilter,
            includeRuntime,
            rootMode);

        var tree = _treeFactory.Create(_formatter.FormatExceptionCallTreeLine(rootSelection.RootNode, rootSelection.RootTotal, isRoot: true, rootLabelOverride));
        AddChildNodes(
            tree,
            rootSelection.RootNode,
            rootSelection.RootTotal,
            includeRuntime,
            childDepth: 1,
            traversal);

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{rootSelection.Title}[/]"),
            tree);
    }

    private void AddChildNodes(
        IHasTreeNodes parent,
        CallTreeNode node,
        double totalCount,
        bool includeRuntime,
        int childDepth,
        CallTreeTraversalSettings traversal)
    {
        if (childDepth > traversal.MaxDepth)
        {
            return;
        }

        var children = GetVisibleChildren(node, includeRuntime, traversal);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var hasVisibleChildren = !isSpecialLeaf &&
                childDepth < traversal.MaxDepth &&
                GetVisibleChildren(child, includeRuntime, traversal).Count > 0;
            var childNode = parent.AddNode(_formatter.FormatExceptionCallTreeLine(
                child,
                totalCount,
                isRoot: false,
                rootLabelOverride: null,
                isLeaf: !hasVisibleChildren));

            if (hasVisibleChildren)
            {
                AddChildNodes(
                    childNode,
                    child,
                    totalCount,
                    includeRuntime,
                    childDepth + 1,
                    traversal);
            }
        }
    }

    private static IReadOnlyList<CallTreeNode> GetVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        CallTreeTraversalSettings traversal)
    {
        return CallTreeVisibility.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime: false,
            traversal.MaxWidth,
            traversal.SiblingCutoffPercent);
    }
}
