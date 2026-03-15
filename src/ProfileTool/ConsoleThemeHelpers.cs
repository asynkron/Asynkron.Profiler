using System;
using System.Globalization;
using Spectre.Console;

namespace Asynkron.Profiler;

internal static class ConsoleThemeHelpers
{
    public static void PrintSection(string text, string? color = null)
    {
        Console.WriteLine();
        if (string.IsNullOrWhiteSpace(color))
        {
            Console.WriteLine(text);
            return;
        }

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }

    public static Color ParseHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Color.Default;
        }

        if (!TryParseHexColor(value, out var rgb))
        {
            throw new FormatException($"Expected a 6-digit hex color, got '{value}'.");
        }

        return new Color(rgb.R, rgb.G, rgb.B);
    }

    public static bool TryParseHexColor(string? value, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(trimmed.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(trimmed.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(trimmed.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = (r, g, b);
        return true;
    }
}
