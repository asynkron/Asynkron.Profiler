using System.Globalization;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed partial class JitOutputFormatter
{
    private readonly Theme _theme;

    public JitOutputFormatter(Theme theme)
    {
        _theme = theme;
    }

    public string ColorizeDisasmLine(string line)
    {
        if (line.Length == 0)
        {
            return line;
        }

        var commentColor = AnsiColor(_theme.TreeGuideColor, dim: true);
        var labelColor = AnsiColor(_theme.CpuCountColor);
        var mnemonicColor = AnsiColor(_theme.TextColor);
        var numberColor = AnsiColor(_theme.LeafHighlightColor);

        if (line.StartsWith(';'))
        {
            return WrapAnsi(line, commentColor);
        }

        var trimmed = line.TrimStart();
        var indent = line[..(line.Length - trimmed.Length)];
        if (trimmed.StartsWith("Runs=", StringComparison.Ordinal) ||
            trimmed.StartsWith("Done in", StringComparison.Ordinal))
        {
            return WrapAnsi(line, commentColor);
        }

        var commentIndex = trimmed.IndexOf(";;", StringComparison.Ordinal);
        var leading = commentIndex >= 0 ? trimmed[..commentIndex] : trimmed;
        var trailing = commentIndex >= 0 ? trimmed[commentIndex..] : string.Empty;

        var labelIndex = leading.IndexOf(':');
        if (labelIndex > 0 && IsLabelToken(leading[..labelIndex]))
        {
            var label = leading[..(labelIndex + 1)];
            var rest = leading[(labelIndex + 1)..];
            var restColored = ColorizeInstructionSegment(rest, mnemonicColor, numberColor);
            var highlighted = $"{WrapAnsi(label, labelColor)}{restColored}";
            if (trailing.Length > 0)
            {
                highlighted += WrapAnsi(trailing, commentColor);
            }

            return $"{indent}{highlighted}";
        }

        var instructionColored = ColorizeInstructionSegment(leading, mnemonicColor, numberColor);
        if (trailing.Length > 0)
        {
            instructionColored += WrapAnsi(trailing, commentColor);
        }

        return $"{indent}{instructionColored}";
    }

    public void PrintDisasmSummary(string logPath)
    {
        var summary = JitDumpSummaryReader.ReadDisasmSummary(logPath);
        if (summary == null)
        {
            return;
        }

        ConsoleThemeHelpers.PrintSection("JIT DISASM SUMMARY", _theme.AccentColor);
        if (string.IsNullOrWhiteSpace(summary.MethodName))
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No disassembly markers found.[/]");
            return;
        }

        PrintSummaryLine("Method:", summary.MethodName);
        if (!string.IsNullOrWhiteSpace(summary.Tier))
        {
            PrintSummaryLine("Tier:", summary.Tier);
        }

        if (!string.IsNullOrWhiteSpace(summary.Target))
        {
            PrintSummaryLine("Target:", summary.Target);
        }

        if (!string.IsNullOrWhiteSpace(summary.PgoLine))
        {
            PrintSummaryLine("PGO:", summary.PgoLine);
        }

        if (summary.InlinePgo.HasValue || summary.InlineSingleBlock.HasValue || summary.InlineNoPgo.HasValue)
        {
            var inlineSummary = string.Create(
                CultureInfo.InvariantCulture,
                $"PGO={summary.InlinePgo ?? 0}, single-block={summary.InlineSingleBlock ?? 0}, no-PGO={summary.InlineNoPgo ?? 0}");
            PrintSummaryLine("Inlinees:", inlineSummary);
        }

        if (summary.CodeSize.HasValue)
        {
            PrintSummaryLine(
                "Code size:",
                string.Create(CultureInfo.InvariantCulture, $"{summary.CodeSize.Value} bytes"));
        }

        if (summary.BlockCount.HasValue)
        {
            PrintSummaryLine("Blocks:", summary.BlockCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (summary.InstructionCount.HasValue)
        {
            PrintSummaryLine(
                "Instructions:",
                summary.InstructionCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (logPath.EndsWith(".color.log", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            PrintSummaryLine("To browse the assembly, run:", $"less {logPath}");
        }
    }

    public void PrintInlineSummary(string logPath)
    {
        var summary = JitDumpSummaryReader.ReadInlineSummary(logPath);
        if (summary == null)
        {
            return;
        }

        ConsoleThemeHelpers.PrintSection("JIT INLINE SUMMARY", _theme.AccentColor);
        if (summary.MethodCount == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No JIT dump markers found.[/]");
            return;
        }

        AnsiConsole.MarkupLine(
            $"[{_theme.AccentColor}]Methods compiled:[/] " +
            $"[{_theme.CpuCountColor}]{summary.MethodCount.ToString(CultureInfo.InvariantCulture)}[/]");
        AnsiConsole.MarkupLine(
            $"[{_theme.AccentColor}]Inlining:[/] " +
            $"[{_theme.CpuCountColor}]" +
            $"success={summary.InlineSuccess.ToString(CultureInfo.InvariantCulture)}, " +
            $"failed={summary.InlineFailed.ToString(CultureInfo.InvariantCulture)}[/]");
    }

    private void PrintSummaryLine(string label, string value)
    {
        AnsiConsole.MarkupLine(
            $"[{_theme.AccentColor}]{Markup.Escape(label)}[/] " +
            $"[{_theme.CpuCountColor}]{Markup.Escape(value)}[/]");
    }

    private static string ColorizeInstructionSegment(string value, string mnemonicColor, string numberColor)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.TrimStart();
        var indent = value[..(value.Length - trimmed.Length)];
        var mnemonicEnd = 0;
        while (mnemonicEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[mnemonicEnd]))
        {
            mnemonicEnd++;
        }

        if (mnemonicEnd == 0)
        {
            return value;
        }

        var mnemonic = trimmed[..mnemonicEnd];
        var rest = trimmed[mnemonicEnd..];
        var restColored = ColorizeNumbers(rest, numberColor);
        return $"{indent}{WrapAnsi(mnemonic, mnemonicColor)}{restColored}";
    }

    private static string ColorizeNumbers(string text, string color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return text;
        }

        return NumberPattern().Replace(text, match => WrapAnsi(match.Value, color));
    }

    private static bool IsLabelToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '$')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string AnsiColor(string hex, bool dim = false)
    {
        if (!TryResolveRgb(hex, out var rgb))
        {
            return string.Empty;
        }

        var dimPrefix = dim ? "\u001b[2m" : string.Empty;
        return $"{dimPrefix}\u001b[38;2;{rgb.R};{rgb.G};{rgb.B}m";
    }

    private static bool TryResolveRgb(string value, out (byte R, byte G, byte B) rgb)
    {
        if (ConsoleThemeHelpers.TryParseHexColor(value, out rgb))
        {
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        rgb = normalized switch
        {
            "yellow" => (255, 255, 0),
            "red" => (255, 0, 0),
            "green" => (0, 255, 0),
            "blue" => (0, 0, 255),
            "cyan" => (0, 255, 255),
            "plum1" => (255, 187, 255),
            _ => default
        };

        return rgb != default;
    }

    private static string WrapAnsi(string text, string color)
    {
        if (string.IsNullOrEmpty(color))
        {
            return text;
        }

        const string Reset = "\u001b[0m";
        return $"{color}{text}{Reset}";
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])", RegexOptions.Compiled)]
    private static partial Regex NumberPattern();
}
