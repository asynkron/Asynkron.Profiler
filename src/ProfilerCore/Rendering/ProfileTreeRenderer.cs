using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Asynkron.Profiler.CallTreeHelpers;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asynkron.Profiler;

internal sealed class ProfileTreeRenderer
{
    private const double HotnessFireThreshold = 0.4d;
    private const double HotnessColorFloor = 0.001d;
    private const double HotnessColorMid = 0.0025d;
    private const double HotnessColorMax = 0.4d;
    private const string HotspotMarker = "\U0001F525";

    private readonly Theme _theme;
    private readonly Style _treeGuideStyle;

    public ProfileTreeRenderer(Theme theme)
    {
        _theme = theme;
        _treeGuideStyle = new Style(ConsoleThemeHelpers.ParseHexColor(_theme.TreeGuideColor));
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
                string.Empty,
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

        var rootLabel = FormatCallTreeLine(
            rootNode,
            rootTotal,
            totalSamples,
            useSelfTime,
            isRoot: true,
            timeUnitLabel: timeUnitLabel,
            countSuffix: countSuffix,
            isLeaf: false,
            timeline: null,
            depth: 0,
            useHeatColor: true);
        var tree = CreateCallTree(rootLabel);
        if (allocationTypeLimit > 0)
        {
            AddAllocationTypeNodes(tree, rootNode, allocationTypeLimit);
        }

        if (exceptionTypeLimit > 0)
        {
            AddExceptionTypeNodes(tree, rootNode, exceptionTypeLimit);
        }

        var children = GetVisibleCallTreeChildren(rootNode, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var childHotness = ComputeHotness(child, rootTotal, totalSamples);
            var isHotspot = IsFireEmojiCandidate(childHotness, hotThreshold);
            var (childNode, isSpecialLeaf) = AddCallTreeChildNode(
                tree.AddNode,
                child,
                rootTotal,
                totalSamples,
                useSelfTime,
                includeRuntime,
                1,
                maxDepth,
                maxWidth,
                siblingCutoffPercent,
                timeUnitLabel,
                countSuffix,
                isHotspot: isHotspot,
                useHeatColor: true);
            AddCallTreeDecorationsAndChildren(
                childNode,
                child,
                allocationTypeLimit,
                exceptionTypeLimit,
                isSpecialLeaf,
                () => AddCallTreeChildren(
                    childNode,
                    child,
                    rootTotal,
                    totalSamples,
                    useSelfTime,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent,
                    timeUnitLabel,
                    countSuffix,
                    allocationTypeLimit,
                    exceptionTypeLimit,
                    hotThreshold,
                    highlightHotspots: true,
                    timeline: null));
        }

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
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

        var rootLabel = FormatCallTreeLine(
            rootNode,
            rootTotal,
            totalSamples,
            useSelfTime: false,
            isRoot: true,
            timeUnitLabel: "ms",
            countSuffix: "x");
        var tree = CreateCallTree(rootLabel);
        var children = CallTreeFilters.GetVisibleChildren(
            rootNode,
            includeRuntime,
            useSelfTime: false,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        foreach (var child in children)
        {
            var (childNode, isSpecialLeaf) = AddCallTreeChildNode(
                tree.AddNode,
                child,
                rootTotal,
                totalSamples,
                useSelfTime: false,
                includeRuntime,
                depth: 1,
                maxDepth,
                maxWidth,
                siblingCutoffPercent,
                timeUnitLabel: "ms",
                countSuffix: "x");
            if (!isSpecialLeaf)
            {
                AddCallTreeChildren(
                    childNode,
                    child,
                    rootTotal,
                    totalSamples,
                    useSelfTime: false,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent,
                    "ms",
                    "x",
                    0,
                    0,
                    HotnessFireThreshold,
                    highlightHotspots: false);
            }
        }

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
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

        var rootLabel = FormatExceptionCallTreeLine(rootNode, rootTotal, isRoot: true, rootLabelOverride);
        var tree = CreateCallTree(rootLabel);
        AddExceptionChildNodes(
            tree.AddNode,
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

    public Tree BuildAllocationCallTree(
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

        return CreateAllocationCallTree(rootNode, includeRuntime, callTreeDepth, callTreeWidth, callTreeSiblingCutoffPercent);
    }

    private (CallTreeNode RootNode, double RootTotal, string Title, int MaxDepth, int MaxWidth, int SiblingCutoffPercent) PrepareCallTreeDisplay(
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

    private Tree CreateCallTree(string rootLabel)
    {
        return new Tree(rootLabel)
        {
            Style = _treeGuideStyle,
            Guide = new CompactTreeGuide()
        };
    }

    private void AddExceptionChildNodes(
        Func<string, TreeNode> addNode,
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

        var children = GetVisibleExceptionChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var hasVisibleChildren = !isSpecialLeaf &&
                childDepth < maxDepth &&
                GetVisibleExceptionChildren(child, includeRuntime, maxWidth, siblingCutoffPercent).Count > 0;
            var childNode = addNode(
                FormatExceptionCallTreeLine(
                    child,
                    totalCount,
                    isRoot: false,
                    rootLabelOverride: null,
                    isLeaf: !hasVisibleChildren));

            if (hasVisibleChildren)
            {
                AddExceptionChildNodes(
                    childNode.AddNode,
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

    private static IReadOnlyList<CallTreeNode> GetVisibleExceptionChildren(
        CallTreeNode node,
        bool includeRuntime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        return CallTreeFilters.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime: false,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
    }

    private Tree CreateAllocationCallTree(
        AllocationCallTreeNode root,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent)
    {
        var rootLabel = FormatAllocationCallTreeLine(root, root.TotalBytes, isRoot: true, isLeaf: false);
        var tree = CreateCallTree(rootLabel);
        var children = GetVisibleAllocationChildren(root, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
            var childChildren = !isSpecialLeaf
                ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
                : [];
            var isLeaf = isSpecialLeaf || maxDepth <= 1 || childChildren.Count == 0;

            var childNode = tree.AddNode(FormatAllocationCallTreeLine(child, root.TotalBytes, isRoot: false, isLeaf));
            if (!isSpecialLeaf)
            {
                AddAllocationCallTreeChildren(
                    childNode,
                    child,
                    root.TotalBytes,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }

        return tree;
    }

    private void AddAllocationCallTreeChildren(
        TreeNode parent,
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

        var children = GetVisibleAllocationChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var nextDepth = depth + 1;
            var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
            var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
                ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
                : [];
            var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

            var childNode = parent.AddNode(FormatAllocationCallTreeLine(child, rootTotalBytes, isRoot: false, isLeaf));
            if (!isSpecialLeaf)
            {
                AddAllocationCallTreeChildren(
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

    private static List<AllocationCallTreeNode> GetVisibleAllocationChildren(
        AllocationCallTreeNode node,
        bool includeRuntime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        var ordered = EnumerateVisibleAllocationChildren(node, includeRuntime)
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

    private static IEnumerable<AllocationCallTreeNode> EnumerateVisibleAllocationChildren(
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

            foreach (var grandChild in EnumerateVisibleAllocationChildren(child, includeRuntime))
            {
                yield return grandChild;
            }
        }
    }

    private string FormatAllocationCallTreeLine(
        AllocationCallTreeNode node,
        long rootTotalBytes,
        bool isRoot,
        bool isLeaf)
    {
        var bytes = node.TotalBytes;
        var pct = rootTotalBytes > 0 ? 100d * bytes / rootTotalBytes : 0d;
        var count = node.Count;
        var bytesText = FormatBytes(bytes);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var countText = count.ToString("N0", CultureInfo.InvariantCulture);

        var displayName = isRoot ? NameFormatter.FormatTypeDisplayName(node.Name) : FormatFunctionDisplayName(node.Name);
        if (displayName.Length > 80)
        {
            displayName = displayName[..77] + "...";
        }

        var nameText = isRoot
            ? $"[{_theme.TextColor}]{Markup.Escape(displayName)}[/]"
            : FormatCallTreeName(displayName, displayName, isLeaf);

        return $"[{_theme.CpuValueColor}]{bytesText}[/] [{_theme.SampleColor}]{pctText}%[/] [{_theme.CpuCountColor}]{countText}x[/] {nameText}";
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
        var (treeText, visibleLength) = FormatCallTreeLineSimple(
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
        var timelineBar = RenderTimelineBar(node, timeline);
        rows.Add((treeText, visibleLength, timelineBar));

        if (depth >= maxDepth)
        {
            return;
        }

        var basePrefix = continuationPrefix ?? prefix;

        var children = GetVisibleCallTreeChildren(node, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var isLast = i == children.Count - 1;
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var childHotness = ComputeHotness(child, totalTime, totalSamples);
            var isChildHotspot = highlightHotspots && IsFireEmojiCandidate(childHotness, hotThreshold);

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
                highlightHotspots: highlightHotspots,
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

    private (string Text, int VisibleLength) FormatCallTreeLineSimple(
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool isRoot,
        string timeUnitLabel,
        string countSuffix,
        string prefix,
        int maxWidth,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var matchName = GetCallTreeMatchName(node);
        var displayName = GetCallTreeDisplayName(matchName);

        if (isHotspot)
        {
            displayName = $"{HotspotMarker} {displayName}";
        }

        var timeSpent = isRoot && useSelfTime
            ? GetCallTreeTime(node, useSelfTime: false)
            : GetCallTreeTime(node, useSelfTime);
        var calls = node.Calls;
        var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
        var timeText = ProfileRenderFormatting.FormatCpuTime(timeSpent, timeUnitLabel);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
        var countText = callsText + countSuffix;

        var statsText = $"{timeText} {timeUnitLabel} {pctText}% {countText} ";
        var statsLength = prefix.Length + statsText.Length;
        var maxNameLength = maxWidth - statsLength - 1;

        var truncatedName = displayName;
        if (maxNameLength > 3 && displayName.Length > maxNameLength)
        {
            truncatedName = displayName[..(maxNameLength - 3)] + "...";
        }
        else if (maxNameLength <= 3)
        {
            truncatedName = "...";
        }

        var hotness = ComputeHotness(node, totalTime, totalSamples);
        var nameColor = useHeatColor ? GetHotnessColor(hotness) : null;
        var nameText = FormatCallTreeName(truncatedName, matchName, ShouldStopAtLeaf(matchName), nameColor);

        var visibleLength = statsLength + truncatedName.Length;

        return ($"[dim]{Markup.Escape(prefix)}[/]" +
                $"[{_theme.CpuValueColor}]{timeText} {timeUnitLabel}[/] " +
                $"[{_theme.SampleColor}]{pctText}%[/] " +
                $"[{_theme.CpuCountColor}]{countText}[/] {nameText}", visibleLength);
    }

    private void AddCallTreeChildren(
        TreeNode parent,
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

        var children = GetVisibleCallTreeChildren(node, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var childHotness = ComputeHotness(child, totalTime, totalSamples);
            var isHotspot = highlightHotspots && IsFireEmojiCandidate(childHotness, hotThreshold);
            var (childNode, isSpecialLeaf) = AddCallTreeChildNode(
                parent.AddNode,
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

    private static bool HasVisibleChildren(
        CallTreeNode node,
        bool includeRuntime,
        bool useSelfTime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        return GetVisibleCallTreeChildren(node, includeRuntime, useSelfTime, maxWidth, siblingCutoffPercent).Count > 0;
    }

    private static IReadOnlyList<CallTreeNode> GetVisibleCallTreeChildren(
        CallTreeNode node,
        bool includeRuntime,
        bool useSelfTime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        return CallTreeFilters.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
    }

    private (TreeNode Node, bool IsSpecialLeaf) AddCallTreeChildNode(
        Func<string, TreeNode> addNode,
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
                     !HasVisibleChildren(
                         child,
                         includeRuntime,
                         useSelfTime,
                         maxWidth,
                         siblingCutoffPercent);

        var childNode = addNode(FormatCallTreeLine(
            child,
            totalTime,
            totalSamples,
            useSelfTime,
            isRoot: false,
            timeUnitLabel: timeUnitLabel,
            countSuffix: countSuffix,
            isLeaf: isLeaf,
            timeline: timeline,
            depth: depth,
            isHotspot: isHotspot,
            useHeatColor: useHeatColor));

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
        if (allocationTypeLimit > 0)
        {
            AddAllocationTypeNodes(childNode, child, allocationTypeLimit);
        }

        if (exceptionTypeLimit > 0)
        {
            AddExceptionTypeNodes(childNode, child, exceptionTypeLimit);
        }

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
            var line = $"[{_theme.MemoryValueColor}]{bytesText}[/] [{_theme.MemoryCountColor}]{countText}[/] {Markup.Escape(typeName)}";
            parent.AddNode(line);
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
            var line = $"[{_theme.ErrorColor}]{countText}[/] {Markup.Escape(typeName)}";
            parent.AddNode(line);
        }
    }

    private string FormatCallTreeLine(
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool isRoot,
        string timeUnitLabel,
        string countSuffix,
        bool isLeaf = false,
        TimelineContext? timeline = null,
        int depth = 0,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var matchName = GetCallTreeMatchName(node);
        var displayName = GetCallTreeDisplayName(matchName);

        if (isHotspot)
        {
            displayName = $"{HotspotMarker} {displayName}";
        }

        var timeSpent = isRoot && useSelfTime
            ? GetCallTreeTime(node, useSelfTime: false)
            : GetCallTreeTime(node, useSelfTime);
        var calls = node.Calls;
        var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
        var timeText = ProfileRenderFormatting.FormatCpuTime(timeSpent, timeUnitLabel);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
        var countText = callsText + countSuffix;
        var hotness = ComputeHotness(node, totalTime, totalSamples);
        var nameColor = useHeatColor ? GetHotnessColor(hotness) : null;

        int maxNameLen;
        if (timeline != null)
        {
            var treeGuideWidth = depth * 4;
            var statsText = $"{timeText} {timeUnitLabel} {pctText}% {countText} ";
            maxNameLen = Math.Max(15, timeline.TextWidth - treeGuideWidth - statsText.Length);
        }
        else
        {
            maxNameLen = 80;
        }

        if (displayName.Length > maxNameLen)
        {
            displayName = displayName[..(maxNameLen - 3)] + "...";
        }

        var nameText = FormatCallTreeName(displayName, matchName, isLeaf, nameColor);

        var baseLine =
            $"[{_theme.CpuValueColor}]{timeText} {timeUnitLabel}[/] " +
            $"[{_theme.SampleColor}]{pctText}%[/] " +
            $"[{_theme.CpuCountColor}]{countText}[/] {nameText}";

        if (timeline != null && node.HasTiming)
        {
            var bar = RenderTimelineBar(node, timeline);
            var visibleLength = EstimateVisibleLength(baseLine);
            var padding = timeline.GetPaddingForDepth(depth, visibleLength);
            var paddedLine = baseLine + new string(' ', padding);
            return $"{paddedLine} [dim]│[/] {bar}";
        }

        return baseLine;
    }

    private static int EstimateVisibleLength(string markup)
    {
        var result = markup;
        while (true)
        {
            var start = result.IndexOf('[');
            if (start < 0)
            {
                break;
            }

            var end = result.IndexOf(']', start);
            if (end < 0)
            {
                break;
            }

            result = result.Remove(start, end - start + 1);
        }

        return result.Length;
    }

    private static string RenderTimelineBar(CallTreeNode node, TimelineContext ctx)
    {
        if (!node.HasTiming || ctx.RootDuration <= 0)
        {
            return new string(' ', ctx.BarWidth);
        }

        var buffer = new char[ctx.BarWidth];
        Array.Fill(buffer, ' ');

        var startOffset = node.MinStart - ctx.RootStart;
        var startRatio = Math.Clamp(startOffset / ctx.RootDuration, 0, 1);
        var durationRatio = Math.Clamp((node.MaxEnd - node.MinStart) / ctx.RootDuration, 0, 1);

        var scaledWidth = ctx.BarWidth * 8;
        var startUnit = (int)Math.Round(startRatio * scaledWidth);
        var durationUnits = Math.Max(1, (int)Math.Round(durationRatio * scaledWidth));
        var endUnit = Math.Min(startUnit + durationUnits, scaledWidth);

        for (var column = 0; column < ctx.BarWidth; column++)
        {
            var columnStart = column * 8;
            var columnEnd = columnStart + 8;
            var overlap = Math.Max(0, Math.Min(columnEnd, endUnit) - Math.Max(columnStart, startUnit));
            if (overlap <= 0)
            {
                continue;
            }

            var includesStart = startUnit >= columnStart && startUnit < columnEnd;
            var includesEnd = endUnit > columnStart && endUnit <= columnEnd;

            buffer[column] = overlap switch
            {
                >= 8 => '█',
                _ when includesStart && !includesEnd => SelectRightBlock(overlap / 8.0),
                _ when includesEnd && !includesStart => SelectLeftBlock(overlap / 8.0),
                _ when includesStart && includesEnd => SelectLeftBlock(overlap / 8.0),
                _ => SelectLeftBlock(overlap / 8.0)
            };
        }

        var pct = durationRatio * 100;
        var color = GetHeatColor(pct);

        return $"[{color}]{new string(buffer)}[/]";
    }

    private static string GetHeatColor(double percentage)
    {
        percentage = Math.Clamp(percentage, 0, 100);

        int r;
        int g;
        int b;

        if (percentage <= 5)
        {
            r = 0;
            g = 200;
            b = 0;
        }
        else if (percentage <= 50)
        {
            var t = (percentage - 5) / 45.0;
            r = (int)(0 + t * 255);
            g = (int)(200 - t * 35);
            b = 0;
        }
        else
        {
            var t = (percentage - 50) / 50.0;
            r = 255;
            g = (int)(165 - t * 165);
            b = 0;
        }

        return $"rgb({r},{g},{b})";
    }

    private static char SelectLeftBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.875 => '▉',
            >= 0.75 => '▊',
            >= 0.625 => '▋',
            >= 0.5 => '▌',
            >= 0.375 => '▍',
            >= 0.25 => '▎',
            >= 0.125 => '▏',
            _ => ' '
        };
    }

    private static char SelectRightBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.5 => '▐',
            >= 0.125 => '▕',
            _ => ' '
        };
    }

    private string FormatExceptionCallTreeLine(
        CallTreeNode node,
        double totalCount,
        bool isRoot,
        string? rootLabelOverride,
        bool isLeaf = false)
    {
        var matchName = GetCallTreeMatchName(node);
        var displayName = isRoot && !string.IsNullOrWhiteSpace(rootLabelOverride)
            ? rootLabelOverride
            : GetCallTreeDisplayName(matchName);
        if (displayName.Length > 80)
        {
            displayName = displayName[..77] + "...";
        }

        var count = isRoot ? totalCount : node.Total;
        var pct = totalCount > 0 ? 100 * count / totalCount : 0;
        var countText = count.ToString("N0", CultureInfo.InvariantCulture);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var nameText = FormatCallTreeName(displayName, matchName, isLeaf);

        return $"[{_theme.CpuValueColor}]{countText}x[/] [{_theme.SampleColor}]{pctText}%[/] {nameText}";
    }

    private string FormatCallTreeName(string displayName, string matchName, bool isLeaf, string? nameColorOverride = null)
    {
        var escaped = Markup.Escape(displayName);
        if (isLeaf && ShouldStopAtLeaf(matchName))
        {
            return $"[{_theme.LeafHighlightColor}]{escaped}[/]";
        }

        var color = string.IsNullOrWhiteSpace(nameColorOverride) ? _theme.TextColor : nameColorOverride;
        return $"[{color}]{escaped}[/]";
    }

    private static bool IsFireEmojiCandidate(double hotness, double hotThreshold) => hotness >= hotThreshold;

    private static string InterpolateColor((byte R, byte G, byte B) start, (byte R, byte G, byte B) end, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        var r = (byte)Math.Round(start.R + (end.R - start.R) * t);
        var g = (byte)Math.Round(start.G + (end.G - start.G) * t);
        var b = (byte)Math.Round(start.B + (end.B - start.B) * t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private string? GetHotnessColor(double hotness)
    {
        if (!ConsoleThemeHelpers.TryParseHexColor(_theme.TextColor, out var cool) ||
            !ConsoleThemeHelpers.TryParseHexColor(_theme.HotColor, out var hot))
        {
            return null;
        }

        double normalizedHotness;
        if (hotness <= HotnessColorFloor)
        {
            normalizedHotness = 0d;
        }
        else if (hotness <= HotnessColorMid)
        {
            var span = HotnessColorMid - HotnessColorFloor;
            normalizedHotness = span > 0d
                ? (hotness - HotnessColorFloor) / span * 0.5d
                : 0d;
        }
        else if (hotness >= HotnessColorMax)
        {
            normalizedHotness = 1d;
        }
        else
        {
            var span = HotnessColorMax - HotnessColorMid;
            normalizedHotness = span > 0d
                ? 0.5d + (hotness - HotnessColorMid) / span * 0.5d
                : 1d;
        }

        normalizedHotness = Math.Clamp(normalizedHotness, 0d, 1d);
        return InterpolateColor(cool, hot, normalizedHotness);
    }
}
