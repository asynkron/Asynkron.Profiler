using System;
using System.Globalization;
using Spectre.Console;

namespace Asynkron.Profiler;

internal static class ProfileRenderFormatting
{
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

    public static string WrapMarkup(string text, string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return text;
        }

        return $"[{color}]{Markup.Escape(text)}[/]";
    }
}
