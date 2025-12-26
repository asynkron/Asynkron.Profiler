using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Asynkron.Profiler;

/// <summary>
/// Renders CPU profile data as a visual timeline with horizontal bars showing
/// when each function executed relative to the total execution time.
/// </summary>
public static class CpuTimelineFormatter
{
    /// <summary>
    /// A span representing a single function invocation with timing information.
    /// </summary>
    public sealed record ProfileSpan(
        string Name,
        double StartMs,
        double EndMs,
        int Depth,
        int FrameIdx)
    {
        public double DurationMs => EndMs - StartMs;
    }

    /// <summary>
    /// Parses a speedscope JSON file and extracts spans with timing information.
    /// </summary>
    public static List<ProfileSpan> ParseSpeedscopeSpans(string speedscopePath)
    {
        var json = File.ReadAllText(speedscopePath);
        return ParseSpeedscopeSpansFromJson(json);
    }

    /// <summary>
    /// Parses speedscope JSON content and extracts spans with timing information.
    /// </summary>
    public static List<ProfileSpan> ParseSpeedscopeSpansFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var frames = root.GetProperty("shared").GetProperty("frames");
        var profile = root.GetProperty("profiles")[0];

        var framesList = new List<string>();
        foreach (var frame in frames.EnumerateArray())
        {
            framesList.Add(frame.GetProperty("name").GetString() ?? "Unknown");
        }

        var spans = new List<ProfileSpan>();
        var stack = new List<(int FrameIdx, double StartAt, int Depth)>();

        if (profile.TryGetProperty("events", out var events))
        {
            foreach (var evt in events.EnumerateArray())
            {
                var eventType = evt.GetProperty("type").GetString();
                var frameIdx = evt.GetProperty("frame").GetInt32();
                var at = evt.GetProperty("at").GetDouble();

                if (string.Equals(eventType, "O", StringComparison.Ordinal)) // Open
                {
                    stack.Add((frameIdx, at, stack.Count));
                }
                else if (string.Equals(eventType, "C", StringComparison.Ordinal)) // Close
                {
                    // Find matching open on stack
                    for (var i = stack.Count - 1; i >= 0; i--)
                    {
                        if (stack[i].FrameIdx == frameIdx)
                        {
                            var (_, startAt, depth) = stack[i];
                            var name = frameIdx < framesList.Count ? framesList[frameIdx] : "Unknown";
                            spans.Add(new ProfileSpan(name, startAt, at, depth, frameIdx));
                            stack.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
        }

        return spans;
    }

    /// <summary>
    /// Formats spans as a visual timeline.
    /// The root span (first span at minimum depth) is rendered full width,
    /// and all children are rendered relative to the root's time range.
    /// </summary>
    /// <param name="spans">The profile spans to render</param>
    /// <param name="width">Width of the timeline bar in characters (default 60)</param>
    /// <param name="maxSpans">Maximum number of spans to render (default 100)</param>
    /// <param name="minDurationMs">Minimum duration in ms to include a span (default 0.1)</param>
    /// <param name="predicate">Optional filter predicate for spans</param>
    public static string Format(
        IEnumerable<ProfileSpan> spans,
        int width = 60,
        int maxSpans = 100,
        double minDurationMs = 0.1,
        Func<ProfileSpan, bool>? predicate = null)
    {
        var output = new StringBuilder();

        var allSpans = spans
            .Where(s => s.DurationMs >= minDurationMs)
            .Where(s => predicate?.Invoke(s) ?? true)
            .OrderBy(s => s.StartMs)
            .ThenBy(s => s.Depth)
            .ToList();

        if (allSpans.Count == 0)
        {
            output.AppendLine("No spans to display.");
            return output.ToString();
        }

        // Find the root span (minimum depth, earliest start)
        var minDepth = allSpans.Min(s => s.Depth);
        var rootSpan = allSpans
            .Where(s => s.Depth == minDepth)
            .OrderBy(s => s.StartMs)
            .First();

        // All timings are relative to the root span
        var rootStart = rootSpan.StartMs;
        var rootDuration = rootSpan.DurationMs;

        if (rootDuration <= 0)
        {
            rootDuration = 1; // Avoid division by zero
        }

        // Filter to only spans within the root's time range and apply maxSpans limit
        var filtered = allSpans
            .Where(s => s.StartMs >= rootStart && s.EndMs <= rootStart + rootDuration)
            .Take(maxSpans)
            .ToList();

        foreach (var span in filtered)
        {
            var isRoot = span == rootSpan;
            output.AppendLine(FormatLine(span, rootStart, rootDuration, width, minDepth, isRoot));
        }

        return output.ToString();
    }

    /// <summary>
    /// Formats a single span as a timeline line.
    /// </summary>
    private static string FormatLine(
        ProfileSpan span,
        double rootStart,
        double rootDuration,
        int width,
        int minDepth,
        bool isRoot)
    {
        var buffer = new char[width];

        if (isRoot)
        {
            // Root span is always full width
            Array.Fill(buffer, '█');
        }
        else
        {
            Array.Fill(buffer, ' ');

            var startOffset = span.StartMs - rootStart;
            var startRatio = Math.Clamp(startOffset / rootDuration, 0, 1);
            var durationRatio = Math.Clamp(span.DurationMs / rootDuration, 0, 1);

            // Use 8 sub-character units per character for smooth rendering
            var scaledWidth = width * 8;
            var startUnit = (int)Math.Round(startRatio * scaledWidth);
            var durationUnits = Math.Max(1, (int)Math.Round(durationRatio * scaledWidth));
            var endUnit = Math.Min(startUnit + durationUnits, scaledWidth);

            for (var column = 0; column < width; column++)
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
        }

        var baseName = ExtractDisplayName(span.Name);
        var depth = Math.Max(0, span.Depth - minDepth);
        var indent = depth == 0 ? string.Empty : new string(' ', depth * 2);

        var label = FormattableString.Invariant($"{indent}{baseName}");
        const int labelWidth = 50;
        if (label.Length > labelWidth)
        {
            label = label[..(labelWidth - 3)] + "...";
        }

        var durationStr = span.DurationMs >= 1000
            ? FormattableString.Invariant($"{span.DurationMs / 1000:F2}s")
            : FormattableString.Invariant($"{span.DurationMs:F1}ms");

        return $"{label,-labelWidth} {durationStr,10} : {new string(buffer)}";
    }

    /// <summary>
    /// Extracts a simplified display name from a full method name.
    /// </summary>
    private static string ExtractDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "(unknown)";
        }

        // Remove generic parameters for brevity
        var genericStart = name.IndexOf('`');
        if (genericStart > 0)
        {
            var genericEnd = name.IndexOf('(', genericStart);
            if (genericEnd > genericStart)
            {
                name = name[..genericStart] + name[genericEnd..];
            }
        }

        // Get just the method name if it's a fully qualified name
        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0 && lastDot < name.Length - 1)
        {
            // Keep class.method format
            var secondLastDot = name.LastIndexOf('.', lastDot - 1);
            if (secondLastDot > 0)
            {
                name = name[(secondLastDot + 1)..];
            }
        }

        // Truncate parameters if too long
        var parenStart = name.IndexOf('(');
        if (parenStart > 0 && name.Length > 60)
        {
            name = name[..parenStart] + "(...)";
        }

        return name;
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
}
