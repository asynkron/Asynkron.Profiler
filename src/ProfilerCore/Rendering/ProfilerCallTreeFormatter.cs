using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed partial class ProfilerCallTreeFormatter
{
    private const double HotnessColorFloor = 0.001d;
    private const double HotnessColorMid = 0.0025d;
    private const double HotnessColorMax = 0.4d;
    private const string HotspotMarker = "\U0001F525";

    private readonly Theme _theme;
    private readonly Regex _jitNumberRegex = JitNumberRegex();

    public ProfilerCallTreeFormatter(Theme theme)
    {
        _theme = theme;
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
        var nameColor = useHeatColor ? GetHotnessColor(hotness) : null;
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
        var nameColor = useHeatColor ? GetHotnessColor(hotness) : null;

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

        var bar = RenderTimelineBar(node, timeline);
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
        return _jitNumberRegex.Replace(text, match => WrapAnsi(match.Value, _theme.AccentColor));
    }

    public static string RenderTimelineBar(CallTreeNode node, TimelineContext context)
    {
        if (!node.HasTiming || context.RootDuration <= 0)
        {
            return new string(' ', context.BarWidth);
        }

        var buffer = new char[context.BarWidth];
        Array.Fill(buffer, ' ');

        var startOffset = node.MinStart - context.RootStart;
        var startRatio = Math.Clamp(startOffset / context.RootDuration, 0, 1);
        var durationRatio = Math.Clamp((node.MaxEnd - node.MinStart) / context.RootDuration, 0, 1);

        var startPosition = startRatio * context.BarWidth;
        var endPosition = (startRatio + durationRatio) * context.BarWidth;

        var leftIndex = (int)Math.Floor(startPosition);
        var rightIndex = (int)Math.Ceiling(endPosition) - 1;
        leftIndex = Math.Clamp(leftIndex, 0, context.BarWidth - 1);
        rightIndex = Math.Clamp(rightIndex, 0, context.BarWidth - 1);

        if (rightIndex < leftIndex)
        {
            rightIndex = leftIndex;
        }

        if (leftIndex == rightIndex)
        {
            var singleFraction = Math.Clamp(endPosition - startPosition, 0, 1);
            buffer[leftIndex] = singleFraction switch
            {
                >= 0.875 => '█',
                >= 0.625 => '▊',
                >= 0.375 => '▌',
                >= 0.125 => '▎',
                _ => ' '
            };
        }
        else
        {
            var leftFraction = 1 - (startPosition - leftIndex);
            buffer[leftIndex] = SelectLeftBlock(leftFraction);

            for (var index = leftIndex + 1; index < rightIndex; index++)
            {
                buffer[index] = '█';
            }

            var rightFraction = endPosition - Math.Floor(endPosition);
            buffer[rightIndex] = rightFraction <= 0 ? '█' : SelectRightBlock(rightFraction);
        }

        var heat = Math.Clamp((node.MaxEnd - node.MinStart) / context.RootDuration, 0, 1);
        var color = GetHeatColor(heat);
        return $"[{color}]{Markup.Escape(new string(buffer))}[/]";
    }

    public static bool IsFireEmojiCandidate(double hotness, double hotThreshold) => hotness >= hotThreshold;

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

    private static string WrapAnsi(string text, string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return text;
        }

        return $"[{color}]{Markup.Escape(text)}[/]";
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

    private static string InterpolateColor((byte R, byte G, byte B) start, (byte R, byte G, byte B) end, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        var r = (byte)Math.Round(start.R + (end.R - start.R) * t);
        var g = (byte)Math.Round(start.G + (end.G - start.G) * t);
        var b = (byte)Math.Round(start.B + (end.B - start.B) * t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string GetHeatColor(double percentage)
    {
        return percentage switch
        {
            >= 0.75 => "red",
            >= 0.50 => "orange1",
            >= 0.25 => "yellow1",
            _ => "grey"
        };
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

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])", RegexOptions.Compiled)]
    private static partial Regex JitNumberRegex();
}
