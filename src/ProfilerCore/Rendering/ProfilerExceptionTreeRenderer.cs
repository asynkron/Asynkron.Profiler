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

    public ProfilerExceptionTreeRenderer(
        Theme theme,
        ProfilerCallTreeFormatter formatter,
        ProfilerTreeFactory treeFactory)
    {
        _theme = theme;
        _formatter = formatter;
        _treeFactory = treeFactory;
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
        maxDepth = Math.Max(1, maxDepth);
        maxWidth = Math.Max(1, maxWidth);
        siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

        var rootNode = callTreeRoot;
        var rootTotal = (double)totalCount;
        if (!string.IsNullOrWhiteSpace(rootFilter))
        {
            var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
            if (matches.Count > 0)
            {
                rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
                rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
                title = $"{title} - root: {Markup.Escape(rootFilter)}";
            }
            else
            {
                AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
            }
        }

        var tree = _treeFactory.Create(_formatter.FormatExceptionCallTreeLine(rootNode, rootTotal, isRoot: true, rootLabelOverride));
        AddChildNodes(
            tree,
            rootNode,
            rootTotal,
            includeRuntime,
            childDepth: 1,
            maxDepth,
            maxWidth,
            siblingCutoffPercent);

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private void AddChildNodes(
        IHasTreeNodes parent,
        CallTreeNode node,
        double totalCount,
        bool includeRuntime,
        int childDepth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent)
    {
        if (childDepth > maxDepth)
        {
            return;
        }

        var children = GetVisibleChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var hasVisibleChildren = !isSpecialLeaf &&
                childDepth < maxDepth &&
                GetVisibleChildren(child, includeRuntime, maxWidth, siblingCutoffPercent).Count > 0;
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
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }
    }

    private static IReadOnlyList<CallTreeNode> GetVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        return CallTreeVisibility.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime: false,
            maxWidth,
            siblingCutoffPercent);
    }
}
