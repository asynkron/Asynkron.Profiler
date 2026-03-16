using System.Globalization;
using System.IO;
using System.Linq;

namespace Asynkron.Profiler;

internal static class JitDumpSummaryReader
{
    public static JitDisasmSummary? ReadDisasmSummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return null;
        }

        string? methodName = null;
        string? tier = null;
        string? target = null;
        string? pgoLine = null;
        int? inlinePgo = null;
        int? inlineSingleBlock = null;
        int? inlineNoPgo = null;
        int? codeSize = null;
        var blocks = 0;
        var instructions = 0;
        var inMethod = false;

        foreach (var line in File.ReadLines(logPath))
        {
            if (line.StartsWith("; Assembly listing for method ", StringComparison.Ordinal))
            {
                if (methodName == null)
                {
                    ParseMethodHeader(
                        line["; Assembly listing for method ".Length..].Trim(),
                        out methodName,
                        out tier);
                    inMethod = true;
                    continue;
                }

                if (inMethod)
                {
                    break;
                }
            }

            if (line.StartsWith("; Emitting ", StringComparison.Ordinal))
            {
                target = line[2..].Trim();
                continue;
            }

            if (line.StartsWith("; No PGO data", StringComparison.Ordinal) ||
                line.Contains("inlinees", StringComparison.Ordinal))
            {
                pgoLine ??= line.TrimStart(' ', ';').Trim();
                ParseInlineCounts(line, ref inlinePgo, ref inlineSingleBlock, ref inlineNoPgo);
                continue;
            }

            if (line.Contains("code size", StringComparison.OrdinalIgnoreCase))
            {
                var digits = new string(line.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                {
                    codeSize ??= size;
                }
            }

            if (!inMethod)
            {
                continue;
            }

            if (line.StartsWith("G_M", StringComparison.Ordinal))
            {
                blocks++;
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == ';')
            {
                continue;
            }

            if (char.IsLetter(trimmed[0]))
            {
                instructions++;
            }
        }

        return new JitDisasmSummary(
            methodName,
            tier,
            target,
            pgoLine,
            inlinePgo,
            inlineSingleBlock,
            inlineNoPgo,
            blocks > 0 ? blocks : null,
            instructions > 0 ? instructions : null,
            codeSize);
    }

    public static JitInlineSummary? ReadInlineSummary(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return null;
        }

        var methodCount = 0;
        var inlineSuccess = 0;
        var inlineFailed = 0;

        foreach (var line in File.ReadLines(logPath))
        {
            if (line.StartsWith("*************** JIT compiling ", StringComparison.Ordinal))
            {
                methodCount++;
                continue;
            }

            if (line.Contains("INLINING SUCCESSFUL", StringComparison.Ordinal))
            {
                inlineSuccess++;
                continue;
            }

            if (line.Contains("INLINING FAILED", StringComparison.Ordinal))
            {
                inlineFailed++;
            }
        }

        return new JitInlineSummary(methodCount, inlineSuccess, inlineFailed);
    }

    private static void ParseMethodHeader(string header, out string? methodName, out string? tier)
    {
        var tierStart = header.LastIndexOf(" (", StringComparison.Ordinal);
        if (tierStart > 0 && header.EndsWith(')'))
        {
            tier = header[(tierStart + 2)..^1];
            methodName = header[..tierStart];
            return;
        }

        methodName = header;
        tier = null;
    }

    private static void ParseInlineCounts(
        string line,
        ref int? inlinePgo,
        ref int? inlineSingleBlock,
        ref int? inlineNoPgo)
    {
        if (!line.Contains("inlinees", StringComparison.Ordinal))
        {
            return;
        }

        var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!int.TryParse(
                    part.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                continue;
            }

            if (part.Contains("inlinees with PGO data", StringComparison.Ordinal))
            {
                inlinePgo = value;
            }
            else if (part.Contains("single block inlinees", StringComparison.Ordinal))
            {
                inlineSingleBlock = value;
            }
            else if (part.Contains("inlinees without PGO data", StringComparison.Ordinal))
            {
                inlineNoPgo = value;
            }
        }
    }
}

internal sealed record JitDisasmSummary(
    string? MethodName,
    string? Tier,
    string? Target,
    string? PgoLine,
    int? InlinePgo,
    int? InlineSingleBlock,
    int? InlineNoPgo,
    int? BlockCount,
    int? InstructionCount,
    int? CodeSize);

internal sealed record JitInlineSummary(int MethodCount, int InlineSuccess, int InlineFailed);
