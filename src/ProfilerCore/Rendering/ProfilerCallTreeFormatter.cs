using System;
using System.Globalization;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerCallTreeFormatter
{
    private const string HotspotMarker = "\U0001F525";

    private readonly Theme _theme;
    private readonly ProfilerHotnessColorScale _hotnessColorScale;
    private readonly ProfilerJitNumberHighlighter _jitNumberHighlighter;

    public ProfilerCallTreeFormatter(Theme theme)
    {
        _theme = theme;
        _hotnessColorScale = new ProfilerHotnessColorScale(theme);
        _jitNumberHighlighter = new ProfilerJitNumberHighlighter(theme);
    }

    public string FormatAllocationCallTreeLine(
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

    public (string Text, int VisibleLength) FormatCallTreeLineSimple(
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
        displayName = PrefixHotspot(displayName, isHotspot);

        var timeSpent = GetDisplayedTime(node, useSelfTime, isRoot);
        var calls = node.Calls;
        var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
        var timeText = FormatCpuTime(timeSpent, timeUnitLabel);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var countText = calls.ToString("N0", CultureInfo.InvariantCulture) + countSuffix;

        var statsText = $"{timeText} {timeUnitLabel} {pctText}% {countText} ";
        var statsLength = prefix.Length + statsText.Length;
        var maxNameLength = maxWidth - statsLength - 1;

        var truncatedName = Truncate(displayName, maxNameLength);
        var hotness = ComputeHotness(node, totalTime, totalSamples);
        var nameColor = useHeatColor ? _hotnessColorScale.GetColor(hotness) : null;
        var nameText = FormatCallTreeName(truncatedName, matchName, ShouldStopAtLeaf(matchName), nameColor);

        var visibleLength = statsLength + truncatedName.Length;

        return ($"[dim]{Markup.Escape(prefix)}[/]" +
                $"[{_theme.CpuValueColor}]{timeText} {timeUnitLabel}[/] " +
                $"[{_theme.SampleColor}]{pctText}%[/] " +
                $"[{_theme.CpuCountColor}]{countText}[/] {nameText}", visibleLength);
    }

    public string FormatCallTreeLine(
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
        var displayName = PrefixHotspot(GetCallTreeDisplayName(matchName), isHotspot);
        var timeSpent = GetDisplayedTime(node, useSelfTime, isRoot);
        var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
        var timeText = FormatCpuTime(timeSpent, timeUnitLabel);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var countText = node.Calls.ToString("N0", CultureInfo.InvariantCulture) + countSuffix;
        var hotness = ComputeHotness(node, totalTime, totalSamples);
        var nameColor = useHeatColor ? _hotnessColorScale.GetColor(hotness) : null;

        var maxNameLength = timeline != null
            ? Math.Max(15, timeline.TextWidth - depth * 4 - $"{timeText} {timeUnitLabel} {pctText}% {countText} ".Length)
            : 80;
        displayName = Truncate(displayName, maxNameLength);

        var nameText = FormatCallTreeName(displayName, matchName, isLeaf, nameColor);
        var baseLine =
            $"[{_theme.CpuValueColor}]{timeText} {timeUnitLabel}[/] " +
            $"[{_theme.SampleColor}]{pctText}%[/] " +
            $"[{_theme.CpuCountColor}]{countText}[/] {nameText}";

        if (timeline == null || !node.HasTiming)
        {
            return baseLine;
        }

        var bar = CallTreeTimelineBarRenderer.Render(node, timeline);
        var visibleLength = EstimateVisibleLength(baseLine);
        var padding = timeline.GetPaddingForDepth(depth, visibleLength);
        return $"{baseLine}{new string(' ', padding)} [dim]│[/] {bar}";
    }

    public string FormatExceptionCallTreeLine(
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
        displayName = Truncate(displayName, 80);

        var count = isRoot ? totalCount : node.Total;
        var pct = totalCount > 0 ? 100 * count / totalCount : 0;
        var countText = count.ToString("N0", CultureInfo.InvariantCulture);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var nameText = FormatCallTreeName(displayName, matchName, isLeaf);

        return $"[{_theme.CpuValueColor}]{countText}x[/] [{_theme.SampleColor}]{pctText}%[/] {nameText}";
    }

    public static string FormatCpuTime(double value, string timeUnitLabel)
    {
        if (string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase))
        {
            var rounded = Math.Round(value, 2);
            var isWhole = Math.Abs(rounded - Math.Round(rounded)) < 0.0001;
            var format = isWhole ? "N0" : "N2";
            return rounded.ToString(format, CultureInfo.InvariantCulture);
        }

        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    public string HighlightJitNumbers(string text)
    {
        return _jitNumberHighlighter.Highlight(text);
    }

    private static double GetDisplayedTime(CallTreeNode node, bool useSelfTime, bool isRoot)
    {
        return isRoot && useSelfTime
            ? GetCallTreeTime(node, useSelfTime: false)
            : GetCallTreeTime(node, useSelfTime);
    }

    private static string PrefixHotspot(string displayName, bool isHotspot)
    {
        return isHotspot ? $"{HotspotMarker} {displayName}" : displayName;
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

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 3)
        {
            return "...";
        }

        return value.Length > maxLength ? value[..(maxLength - 3)] + "..." : value;
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
}
