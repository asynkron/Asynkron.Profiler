using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private readonly ProfilerExceptionTreeRenderer _exceptionRenderer;
    private readonly ProfilerAllocationTreeRenderer _allocationRenderer;

    public ProfilerCallTreeRenderer(Theme theme, ProfilerCallTreeFormatter formatter)
    {
        _theme = theme;
        _formatter = formatter;
        _treeFactory = new ProfilerTreeFactory(theme);
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
        var (rootNode, rootTotal, title, normalizedMaxDepth, normalizedMaxWidth, normalizedSiblingCutoffPercent) = PrepareCallTreeDisplay(
            callTreeRoot,
            totalTime,
            useSelfTime ? "Call Tree (Self Time)" : "Call Tree (Total Time)",
            rootFilter,
            includeRuntime,
            maxDepth,
            maxWidth,
            rootMode,
            siblingCutoffPercent);
        maxDepth = normalizedMaxDepth;
        maxWidth = normalizedMaxWidth;
        siblingCutoffPercent = normalizedSiblingCutoffPercent;

        if (showTimeline && rootNode.HasTiming)
        {
            var terminalWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 160;
            var actualTimelineWidth = Math.Max(20, timelineWidth);
            var treeColumnWidth = terminalWidth - actualTimelineWidth - 2;
            var timeline = new TimelineContext
            {
                RootStart = rootNode.MinStart,
                RootEnd = rootNode.MaxEnd,
                BarWidth = actualTimelineWidth,
                TextWidth = treeColumnWidth,
                MaxNameLength = 200,
                MaxDepth = maxDepth
            };

            var rows = new List<(string TreeText, int VisibleLength, string TimelineBar)>();
            CollectTimelineRows(
                rows,
                rootNode,
                rootTotal,
                totalSamples,
                useSelfTime,
                timeUnitLabel,
                countSuffix,
                "",
                true,
                isHotspot: false,
                highlightHotspots: true,
                includeRuntime,
                0,
                maxDepth,
                maxWidth,
                siblingCutoffPercent,
                hotThreshold,
                timeline);

            var outputLines = new List<IRenderable> { new Markup($"[bold {_theme.AccentColor}]{title}[/]") };
            foreach (var (treeText, visibleLength, timelineBar) in rows)
            {
                var padding = Math.Max(0, treeColumnWidth - visibleLength);
                outputLines.Add(new Markup($"{treeText}{new string(' ', padding)}{timelineBar}"));
            }

            return new Rows(outputLines);
        }

        return BuildStandardTreeRows(
            rootNode,
            rootTotal,
            totalSamples,
            title,
            useSelfTime,
            includeRuntime,
            maxDepth,
            maxWidth,
            siblingCutoffPercent,
            timeUnitLabel,
            countSuffix,
            allocationTypeLimit,
            exceptionTypeLimit,
            hotThreshold,
            highlightHotspots: true);
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
        var (rootNode, rootTotal, title, normalizedMaxDepth, normalizedMaxWidth, normalizedSiblingCutoffPercent) = PrepareCallTreeDisplay(
            callTreeRoot,
            totalTime,
            "Call Tree (Wait Time)",
            rootFilter,
            includeRuntime,
            maxDepth,
            maxWidth,
            rootMode,
            siblingCutoffPercent);
        maxDepth = normalizedMaxDepth;
        maxWidth = normalizedMaxWidth;
        siblingCutoffPercent = normalizedSiblingCutoffPercent;

        return BuildStandardTreeRows(
            rootNode,
            rootTotal,
            totalSamples,
            title,
            useSelfTime: false,
            includeRuntime,
            maxDepth,
            maxWidth,
            siblingCutoffPercent,
            timeUnitLabel: "ms",
            countSuffix: "x",
            allocationTypeLimit: 0,
            exceptionTypeLimit: 0,
            hotThreshold: DefaultHotThreshold,
            highlightHotspots: false);
    }

    public Rows BuildExceptionCallTree(
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
        return _exceptionRenderer.Build(
            callTreeRoot,
            totalCount,
            title,
            rootLabelOverride,
            rootFilter,
            includeRuntime,
            maxDepth,
            maxWidth,
            rootMode,
            siblingCutoffPercent);
    }

    public void PrintAllocationCallTree(
        AllocationCallTreeResult callTree,
        string? callTreeRoot,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        int callTreeSiblingCutoffPercent)
    {
        AnsiConsole.Write(_allocationRenderer.Build(
            callTree,
            callTreeRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeSiblingCutoffPercent));
    }

    public string HighlightJitNumbers(string text) => _formatter.HighlightJitNumbers(text);

    private Rows BuildStandardTreeRows(
        CallTreeNode rootNode,
        double rootTotal,
        double totalSamples,
        string title,
        bool useSelfTime,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool highlightHotspots)
    {
        var rootLabel = _formatter.FormatCallTreeLine(
            rootNode,
            rootTotal,
            totalSamples,
            useSelfTime,
            isRoot: true,
            timeUnitLabel,
            countSuffix,
            isLeaf: false,
            timeline: null,
            depth: 0,
            useHeatColor: true);
        var tree = _treeFactory.Create(rootLabel);

        AddAllocationTypeNodes(tree, rootNode, allocationTypeLimit);
        AddExceptionTypeNodes(tree, rootNode, exceptionTypeLimit);
        AddCallTreeChildren(
            tree,
            rootNode,
            rootTotal,
            totalSamples,
            useSelfTime,
            includeRuntime,
            depth: 1,
            maxDepth,
            maxWidth,
            siblingCutoffPercent,
            timeUnitLabel,
            countSuffix,
            allocationTypeLimit,
            exceptionTypeLimit,
            hotThreshold,
            highlightHotspots);

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private (CallTreeNode RootNode, double RootTotal, string Title, int MaxDepth, int MaxWidth, int SiblingCutoffPercent)
        PrepareCallTreeDisplay(
            CallTreeNode callTreeRoot,
            double totalTime,
            string title,
            string? rootFilter,
            bool includeRuntime,
            int maxDepth,
            int maxWidth,
            string? rootMode,
            int siblingCutoffPercent)
    {
        var normalizedMaxDepth = Math.Max(1, maxDepth);
        var normalizedMaxWidth = Math.Max(1, maxWidth);
        var normalizedSiblingCutoffPercent = Math.Max(0, siblingCutoffPercent);
        var (rootNode, rootTotal, resolvedTitle) = ResolveCallTreeRoot(
            callTreeRoot,
            totalTime,
            title,
            rootFilter,
            includeRuntime,
            rootMode);

        return (rootNode, rootTotal, resolvedTitle, normalizedMaxDepth, normalizedMaxWidth, normalizedSiblingCutoffPercent);
    }

    private void CollectTimelineRows(
        List<(string TreeText, int VisibleLength, string TimelineBar)> rows,
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        string timeUnitLabel,
        string countSuffix,
        string prefix,
        bool isRoot,
        bool isHotspot,
        bool highlightHotspots,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent,
        double hotThreshold,
        TimelineContext timeline,
        string? continuationPrefix = null)
    {
        var (treeText, visibleLength) = _formatter.FormatCallTreeLineSimple(
            node,
            totalTime,
            totalSamples,
            useSelfTime,
            isRoot,
            timeUnitLabel,
            countSuffix,
            prefix,
            timeline.TextWidth,
            isHotspot,
            useHeatColor: true);
        var timelineBar = ProfilerCallTreeFormatter.RenderTimelineBar(node, timeline);
        rows.Add((treeText, visibleLength, timelineBar));

        if (depth >= maxDepth)
        {
            return;
        }

        var basePrefix = continuationPrefix ?? prefix;
        var children = CallTreeVisibility.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent);

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var isLast = index == children.Count - 1;
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var childHotness = ComputeHotness(child, totalTime, totalSamples);
            var isChildHotspot = highlightHotspots && ProfilerCallTreeFormatter.IsFireEmojiCandidate(childHotness, hotThreshold);
            var connector = isLast ? "└─ " : "├─ ";
            var continuation = isLast ? "   " : "│  ";

            CollectTimelineRows(
                rows,
                child,
                totalTime,
                totalSamples,
                useSelfTime,
                timeUnitLabel,
                countSuffix,
                basePrefix + connector,
                isRoot: false,
                isHotspot: isChildHotspot,
                highlightHotspots,
                includeRuntime,
                depth + 1,
                isSpecialLeaf ? depth + 1 : maxDepth,
                maxWidth,
                siblingCutoffPercent,
                hotThreshold,
                timeline,
                basePrefix + continuation);
        }
    }

    private void AddCallTreeChildren(
        IHasTreeNodes parent,
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool highlightHotspots = false,
        TimelineContext? timeline = null)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var children = CallTreeVisibility.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent);

        foreach (var child in children)
        {
            var childHotness = ComputeHotness(child, totalTime, totalSamples);
            var isHotspot = highlightHotspots && ProfilerCallTreeFormatter.IsFireEmojiCandidate(childHotness, hotThreshold);
            var (childNode, isSpecialLeaf) = AddCallTreeChildNode(
                parent,
                child,
                totalTime,
                totalSamples,
                useSelfTime,
                includeRuntime,
                depth,
                maxDepth,
                maxWidth,
                siblingCutoffPercent,
                timeUnitLabel,
                countSuffix,
                timeline,
                isHotspot,
                highlightHotspots);
            AddCallTreeDecorationsAndChildren(
                childNode,
                child,
                allocationTypeLimit,
                exceptionTypeLimit,
                isSpecialLeaf,
                () => AddCallTreeChildren(
                    childNode,
                    child,
                    totalTime,
                    totalSamples,
                    useSelfTime,
                    includeRuntime,
                    depth + 1,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent,
                    timeUnitLabel,
                    countSuffix,
                    allocationTypeLimit,
                    exceptionTypeLimit,
                    hotThreshold,
                    highlightHotspots,
                    timeline));
        }
    }

    private (TreeNode Node, bool IsSpecialLeaf) AddCallTreeChildNode(
        IHasTreeNodes parent,
        CallTreeNode child,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        TimelineContext? timeline = null,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || depth >= maxDepth ||
                     !CallTreeVisibility.HasVisibleChildren(
                         child,
                         includeRuntime,
                         useSelfTime,
                         maxWidth,
                         siblingCutoffPercent);

        var childNode = parent.AddNode(_formatter.FormatCallTreeLine(
            child,
            totalTime,
            totalSamples,
            useSelfTime,
            isRoot: false,
            timeUnitLabel,
            countSuffix,
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
        int allocationTypeLimit,
        int exceptionTypeLimit,
        bool isSpecialLeaf,
        Action addChildren)
    {
        AddAllocationTypeNodes(childNode, child, allocationTypeLimit);
        AddExceptionTypeNodes(childNode, child, exceptionTypeLimit);

        if (!isSpecialLeaf)
        {
            addChildren();
        }
    }

    private (CallTreeNode RootNode, double RootTotal, string Title) ResolveCallTreeRoot(
        CallTreeNode callTreeRoot,
        double totalTime,
        string title,
        string? rootFilter,
        bool includeRuntime,
        string? rootMode)
    {
        var rootNode = callTreeRoot;
        var rootTotal = totalTime;
        if (string.IsNullOrWhiteSpace(rootFilter))
        {
            return (rootNode, rootTotal, title);
        }

        var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
            return (rootNode, rootTotal, title);
        }

        rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
        rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
        return (rootNode, rootTotal, $"{title} - root: {Markup.Escape(rootFilter)}");
    }

    private void AddAllocationTypeNodes(IHasTreeNodes parent, CallTreeNode node, int limit)
    {
        if (limit <= 0 || node.AllocationByType == null || node.AllocationByType.Count == 0)
        {
            return;
        }

        foreach (var entry in node.AllocationByType.OrderByDescending(kv => kv.Value).Take(limit))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Key);
            var bytesText = FormatBytes(entry.Value);
            var count = node.AllocationCountByType != null &&
                        node.AllocationCountByType.TryGetValue(entry.Key, out var allocationCount)
                ? allocationCount
                : 0;
            var countText = count > 0
                ? count.ToString("N0", CultureInfo.InvariantCulture) + "x"
                : "0x";
            parent.AddNode($"[{_theme.MemoryValueColor}]{bytesText}[/] [{_theme.MemoryCountColor}]{countText}[/] {Markup.Escape(typeName)}");
        }
    }

    private void AddExceptionTypeNodes(IHasTreeNodes parent, CallTreeNode node, int limit)
    {
        if (limit <= 0 || node.ExceptionByType == null || node.ExceptionByType.Count == 0)
        {
            return;
        }

        foreach (var entry in node.ExceptionByType.OrderByDescending(kv => kv.Value).Take(limit))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Key);
            var countText = entry.Value.ToString("N0", CultureInfo.InvariantCulture) + "x";
            parent.AddNode($"[{_theme.ErrorColor}]{countText}[/] {Markup.Escape(typeName)}");
        }
    }
}
