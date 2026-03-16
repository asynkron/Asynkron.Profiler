using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerCallTreeRenderer
{
    private const double DefaultHotThreshold = 0.4d;

    private readonly Theme _theme;
    private readonly ProfilerCallTreeFormatter _formatter;
    private readonly ProfilerTreeFactory _treeFactory;
    private readonly ProfilerCallTreeRootResolver _rootResolver;
    private readonly ProfilerCallTreeNodeDecorator _nodeDecorator;
    private readonly ProfilerExceptionTreeRenderer _exceptionRenderer;
    private readonly ProfilerAllocationTreeRenderer _allocationRenderer;

    public ProfilerCallTreeRenderer(Theme theme, ProfilerCallTreeFormatter formatter)
    {
        _theme = theme;
        _formatter = formatter;
        _treeFactory = new ProfilerTreeFactory(theme);
        _rootResolver = new ProfilerCallTreeRootResolver(theme);
        _nodeDecorator = new ProfilerCallTreeNodeDecorator(theme);
        _exceptionRenderer = new ProfilerExceptionTreeRenderer(theme, formatter, _treeFactory);
        _allocationRenderer = new ProfilerAllocationTreeRenderer(formatter, _treeFactory);
    }

    public Rows BuildCpuCallTree(
        CpuProfileResult results,
        bool useSelfTime,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool showTimeline = false,
        int timelineWidth = 40)
    {
        var callTreeRoot = results.CallTreeRoot;
        var totalTime = results.CallTreeTotal;
        var totalSamples = callTreeRoot.Calls;
        var traversal = CallTreeTraversalSettings.Create(maxDepth, maxWidth, siblingCutoffPercent);
        var rootSelection = _rootResolver.Resolve(
            callTreeRoot,
            totalTime,
            useSelfTime ? "Call Tree (Self Time)" : "Call Tree (Total Time)",
            rootFilter,
            includeRuntime,
            rootMode);
        var context = new ProfilerCallTreeRenderContext(
            rootSelection.RootTotal,
            totalSamples,
            useSelfTime,
            includeRuntime,
            traversal,
            timeUnitLabel,
            countSuffix,
            allocationTypeLimit,
            exceptionTypeLimit,
            hotThreshold,
            HighlightHotspots: true);

        if (showTimeline && rootSelection.RootNode.HasTiming)
        {
            var terminalWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 160;
            var actualTimelineWidth = Math.Max(20, timelineWidth);
            var treeColumnWidth = terminalWidth - actualTimelineWidth - 2;
            var timeline = new TimelineContext
            {
                RootStart = rootSelection.RootNode.MinStart,
                RootEnd = rootSelection.RootNode.MaxEnd,
                BarWidth = actualTimelineWidth,
                TextWidth = treeColumnWidth,
                MaxNameLength = 200,
                MaxDepth = traversal.MaxDepth
            };

            var rows = new List<(string TreeText, int VisibleLength, string TimelineBar)>();
            CollectTimelineRows(
                rows,
                rootSelection.RootNode,
                context,
                "",
                true,
                isHotspot: false,
                0,
                timeline);

            var outputLines = new List<IRenderable> { new Markup($"[bold {_theme.AccentColor}]{rootSelection.Title}[/]") };
            foreach (var (treeText, visibleLength, timelineBar) in rows)
            {
                var padding = Math.Max(0, treeColumnWidth - visibleLength);
                outputLines.Add(new Markup($"{treeText}{new string(' ', padding)}{timelineBar}"));
            }

            return new Rows(outputLines);
        }

        return BuildStandardTreeRows(rootSelection.RootNode, rootSelection.Title, context);
    }

    public Rows BuildContentionCallTree(
        ContentionProfileResult results,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent)
    {
        var callTreeRoot = results.CallTreeRoot;
        var totalTime = results.TotalWaitMs;
        var totalSamples = callTreeRoot.Calls;
        var traversal = CallTreeTraversalSettings.Create(maxDepth, maxWidth, siblingCutoffPercent);
        var rootSelection = _rootResolver.Resolve(
            callTreeRoot,
            totalTime,
            "Call Tree (Wait Time)",
            rootFilter,
            includeRuntime,
            rootMode);
        var context = new ProfilerCallTreeRenderContext(
            rootSelection.RootTotal,
            totalSamples,
            false,
            includeRuntime,
            traversal,
            "ms",
            "x",
            0,
            0,
            DefaultHotThreshold,
            false);

        return BuildStandardTreeRows(rootSelection.RootNode, rootSelection.Title, context);
    }

    public Rows BuildExceptionCallTree(ProfilerExceptionCallTreeRequest request)
    {
        return _exceptionRenderer.Build(request);
    }

    public void PrintAllocationCallTree(ProfilerAllocationCallTreeRequest request)
    {
        AnsiConsole.Write(_allocationRenderer.Build(request));
    }

    public string HighlightJitNumbers(string text) => _formatter.HighlightJitNumbers(text);

    private Rows BuildStandardTreeRows(
        CallTreeNode rootNode,
        string title,
        ProfilerCallTreeRenderContext context)
    {
        var rootLabel = _formatter.FormatCallTreeLine(
            rootNode,
            context.RootTotal,
            context.TotalSamples,
            context.UseSelfTime,
            isRoot: true,
            context.TimeUnitLabel,
            context.CountSuffix,
            isLeaf: false,
            timeline: null,
            depth: 0,
            useHeatColor: true);
        var tree = _treeFactory.Create(rootLabel);

        _nodeDecorator.Decorate(tree, rootNode, context);
        AddCallTreeChildren(
            tree,
            rootNode,
            context,
            depth: 1,
            timeline: null);

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private void CollectTimelineRows(
        List<(string TreeText, int VisibleLength, string TimelineBar)> rows,
        CallTreeNode node,
        ProfilerCallTreeRenderContext context,
        string prefix,
        bool isRoot,
        bool isHotspot,
        int depth,
        TimelineContext timeline,
        string? continuationPrefix = null,
        bool stopAfterCurrent = false)
    {
        var (treeText, visibleLength) = _formatter.FormatCallTreeLineSimple(
            node,
            context.RootTotal,
            context.TotalSamples,
            context.UseSelfTime,
            isRoot,
            context.TimeUnitLabel,
            context.CountSuffix,
            prefix,
            timeline.TextWidth,
            isHotspot,
            useHeatColor: true);
        var timelineBar = ProfilerCallTreeFormatter.RenderTimelineBar(node, timeline);
        rows.Add((treeText, visibleLength, timelineBar));

        if (stopAfterCurrent || depth >= context.Traversal.MaxDepth)
        {
            return;
        }

        var basePrefix = continuationPrefix ?? prefix;
        var children = CallTreeVisibility.GetVisibleChildren(
            node,
            context.IncludeRuntime,
            context.UseSelfTime,
            context.Traversal.MaxWidth,
            context.Traversal.SiblingCutoffPercent);

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var isLast = index == children.Count - 1;
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var childHotness = ComputeHotness(child, context.RootTotal, context.TotalSamples);
            var isChildHotspot = context.HighlightHotspots && ProfilerCallTreeFormatter.IsFireEmojiCandidate(childHotness, context.HotThreshold);
            var connector = isLast ? "└─ " : "├─ ";
            var continuation = isLast ? "   " : "│  ";

            CollectTimelineRows(
                rows,
                child,
                context,
                basePrefix + connector,
                isRoot: false,
                isHotspot: isChildHotspot,
                depth + 1,
                timeline,
                basePrefix + continuation,
                isSpecialLeaf);
        }
    }

    private void AddCallTreeChildren(
        IHasTreeNodes parent,
        CallTreeNode node,
        ProfilerCallTreeRenderContext context,
        int depth,
        TimelineContext? timeline = null)
    {
        if (depth > context.Traversal.MaxDepth)
        {
            return;
        }

        var children = CallTreeVisibility.GetVisibleChildren(
            node,
            context.IncludeRuntime,
            context.UseSelfTime,
            context.Traversal.MaxWidth,
            context.Traversal.SiblingCutoffPercent);

        foreach (var child in children)
        {
            var childHotness = ComputeHotness(child, context.RootTotal, context.TotalSamples);
            var isHotspot = context.HighlightHotspots && ProfilerCallTreeFormatter.IsFireEmojiCandidate(childHotness, context.HotThreshold);
            var (childNode, isSpecialLeaf) = AddCallTreeChildNode(
                parent,
                child,
                context,
                depth,
                timeline,
                isHotspot);
            AddCallTreeDecorationsAndChildren(
                childNode,
                child,
                context,
                isSpecialLeaf,
                () => AddCallTreeChildren(
                    childNode,
                    child,
                    context,
                    depth + 1,
                    timeline));
        }
    }

    private (TreeNode Node, bool IsSpecialLeaf) AddCallTreeChildNode(
        IHasTreeNodes parent,
        CallTreeNode child,
        ProfilerCallTreeRenderContext context,
        int depth,
        TimelineContext? timeline = null,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || depth >= context.Traversal.MaxDepth ||
                     !CallTreeVisibility.HasVisibleChildren(
                         child,
                         context.IncludeRuntime,
                         context.UseSelfTime,
                         context.Traversal.MaxWidth,
                         context.Traversal.SiblingCutoffPercent);

        var childNode = parent.AddNode(_formatter.FormatCallTreeLine(
            child,
            context.RootTotal,
            context.TotalSamples,
            context.UseSelfTime,
            isRoot: false,
            context.TimeUnitLabel,
            context.CountSuffix,
            isLeaf,
            timeline,
            depth,
            isHotspot,
            useHeatColor));

        return (childNode, isSpecialLeaf);
    }

    private void AddCallTreeDecorationsAndChildren(
        TreeNode childNode,
        CallTreeNode child,
        ProfilerCallTreeRenderContext context,
        bool isSpecialLeaf,
        Action addChildren)
    {
        _nodeDecorator.Decorate(childNode, child, context);

        if (!isSpecialLeaf)
        {
            addChildren();
        }
    }
}
