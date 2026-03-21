using System;

namespace Asynkron.Profiler;

internal sealed class ProfilerHotnessColorScale
{
    private const double HotnessColorFloor = 0.001d;
    private const double HotnessColorMid = 0.0025d;
    private const double HotnessColorMax = 0.4d;

    private readonly Theme _theme;

    public ProfilerHotnessColorScale(Theme theme)
    {
        _theme = theme;
    }

    public string? GetColor(double hotness)
    {
        if (!ConsoleThemeHelpers.TryParseHexColor(_theme.TextColor, out var cool) ||
            !ConsoleThemeHelpers.TryParseHexColor(_theme.HotColor, out var hot))
        {
            return null;
        }

        var normalizedHotness = Normalize(hotness);
        return InterpolateColor(cool, hot, normalizedHotness);
    }

    public static bool IsHotspot(double hotness, double hotThreshold) => hotness >= hotThreshold;

    private static double Normalize(double hotness)
    {
        if (hotness <= HotnessColorFloor)
        {
            return 0d;
        }

        if (hotness <= HotnessColorMid)
        {
            var span = HotnessColorMid - HotnessColorFloor;
            return span > 0d
                ? (hotness - HotnessColorFloor) / span * 0.5d
                : 0d;
        }

        if (hotness >= HotnessColorMax)
        {
            return 1d;
        }

        var upperSpan = HotnessColorMax - HotnessColorMid;
        return upperSpan > 0d
            ? 0.5d + (hotness - HotnessColorMid) / upperSpan * 0.5d
            : 1d;
    }

    private static string InterpolateColor((byte R, byte G, byte B) start, (byte R, byte G, byte B) end, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        var r = (byte)Math.Round(start.R + (end.R - start.R) * t);
        var g = (byte)Math.Round(start.G + (end.G - start.G) * t);
        var b = (byte)Math.Round(start.B + (end.B - start.B) * t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
