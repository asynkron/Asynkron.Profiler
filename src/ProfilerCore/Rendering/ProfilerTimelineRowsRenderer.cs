using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerTimelineRowsRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeFormatter _formatter;

    public ProfilerTimelineRowsRenderer(Theme theme, ProfilerCallTreeFormatter formatter)
    {
        _theme = theme;
        _formatter = formatter;
    }

    public Rows Build(
        CallTreeNode rootNode,
        string title,
        ProfilerCallTreeRenderContext context,
        int timelineWidth)
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
            MaxDepth = context.Traversal.MaxDepth
        };

        var rows = new List<(string TreeText, int VisibleLength, string TimelineBar)>();
        CollectRows(
            rows,
            rootNode,
            context,
            prefix: string.Empty,
            isRoot: true,
            isHotspot: false,
            depth: 0,
            timeline);

        var outputLines = new List<IRenderable> { new Markup($"[bold {_theme.AccentColor}]{title}[/]") };
        foreach (var (treeText, visibleLength, timelineBar) in rows)
        {
            var padding = Math.Max(0, treeColumnWidth - visibleLength);
            outputLines.Add(new Markup($"{treeText}{new string(' ', padding)}{timelineBar}"));
        }

        return new Rows(outputLines);
    }

    private void CollectRows(
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
        var timelineBar = CallTreeTimelineBarRenderer.Render(node, timeline);
        rows.Add((treeText, visibleLength, timelineBar));

        if (stopAfterCurrent || depth >= context.Traversal.MaxDepth)
        {
            return;
        }

        var basePrefix = continuationPrefix ?? prefix;
        var children = CallTreeFilters.GetVisibleChildren(
            node,
            context.IncludeRuntime,
            context.UseSelfTime,
            context.Traversal.MaxWidth,
            context.Traversal.SiblingCutoffPercent,
            CallTreeHelpers.IsRuntimeNoise);

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var isLast = index == children.Count - 1;
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var childHotness = ComputeHotness(child, context.RootTotal, context.TotalSamples);
            var isChildHotspot = context.HighlightHotspots &&
                                 ProfilerHotnessColorScale.IsHotspot(childHotness, context.HotThreshold);
            var connector = isLast ? "└─ " : "├─ ";
            var continuation = isLast ? "   " : "│  ";

            CollectRows(
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
}
