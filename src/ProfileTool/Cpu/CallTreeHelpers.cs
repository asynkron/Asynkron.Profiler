using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Asynkron.Profiler;

public static class CallTreeHelpers
{
    public static double GetCallTreeTime(CallTreeNode node, bool useSelfTime)
    {
        return useSelfTime ? node.Self : node.Total;
    }

    public static double ComputeHotness(CallTreeNode node, double totalTime, double totalSamples)
    {
        if (totalTime <= 0 || totalSamples <= 0)
        {
            return 0;
        }

        var sampleRatio = node.Calls / totalSamples;
        var selfRatio = node.Self / totalTime;
        return sampleRatio * selfRatio;
    }

    public static List<CallTreeMatch> FindCallTreeMatches(CallTreeNode node, string filter)
    {
        var matches = new List<CallTreeMatch>();
        var normalizedFilter = filter.Trim();
        if (normalizedFilter.Length == 0)
        {
            return matches;
        }

        var order = 0;

        void Visit(CallTreeNode current, int depth)
        {
            if (current.FrameIdx >= 0)
            {
                var displayName = FormatFunctionDisplayName(current.Name);
                if (displayName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                    current.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new CallTreeMatch(current, depth, order++));
                }
            }

            foreach (var child in current.Children.Values)
            {
                Visit(child, depth + 1);
            }
        }

        Visit(node, 0);
        return matches;
    }

    public static CallTreeNode SelectRootMatch(
        IReadOnlyList<CallTreeMatch> matches,
        bool includeRuntime,
        string? rootMode)
    {
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("No call tree matches available.");
        }

        var mode = NormalizeRootMode(rootMode);
        var candidates = includeRuntime
            ? matches
            : matches.Where(match => !IsRuntimeNoise(match.Node.Name)).ToList();
        if (candidates.Count == 0)
        {
            candidates = matches.ToList();
        }

        return mode switch
        {
            "first" or "shallowest" => candidates
                .OrderBy(match => match.Depth)
                .ThenBy(match => match.Order)
                .Select(match => match.Node)
                .First(),
            _ => candidates
                .OrderByDescending(match => GetCallTreeTime(match.Node, useSelfTime: false))
                .Select(match => match.Node)
                .First()
        };
    }

    public static IReadOnlyList<(string Filter, string DisplayName, double Hotness)> CollectHotMethods(
        CallTreeNode rootNode,
        double totalTime,
        double totalSamples,
        bool includeRuntime,
        double hotThreshold)
    {
        var hotMethods = new Dictionary<string, (string DisplayName, double Hotness)>(StringComparer.OrdinalIgnoreCase);
        if (totalTime <= 0 || totalSamples <= 0)
        {
            return Array.Empty<(string Filter, string DisplayName, double Hotness)>();
        }

        void Visit(CallTreeNode node)
        {
            if (node.FrameIdx >= 0 &&
                (includeRuntime || !IsRuntimeNoise(node.Name)))
            {
                var matchName = GetCallTreeMatchName(node);
                if (!IsUnmanagedFrame(matchName))
                {
                    var hotness = ComputeHotness(node, totalTime, totalSamples);
                    if (hotness >= hotThreshold)
                    {
                        var filterName = BuildJitMethodFilter(node.Name);
                        if (!hotMethods.TryGetValue(filterName, out var existing) || hotness > existing.Hotness)
                        {
                            hotMethods[filterName] = (matchName, hotness);
                        }
                    }
                }
            }

            foreach (var child in node.Children.Values)
            {
                Visit(child);
            }
        }

        Visit(rootNode);

        return hotMethods
            .Select(entry => (Filter: entry.Key, DisplayName: entry.Value.DisplayName, Hotness: entry.Value.Hotness))
            .OrderByDescending(entry => entry.Hotness)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeRootMode(string? rootMode)
    {
        if (string.IsNullOrWhiteSpace(rootMode))
        {
            return "hottest";
        }

        return rootMode.Trim().ToLowerInvariant();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024d).ToString("F2", CultureInfo.InvariantCulture) + " KB";
        }

        if (bytes < 1024L * 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / (1024d * 1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " GB";
    }

    public static bool IsUnmanagedFrame(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (FrameNameClassifier.IsExplicitUnmanagedCode(trimmed))
        {
            return true;
        }

        return !FrameNameClassifier.ContainsLetter(trimmed);
    }

    public static string FormatFunctionDisplayName(string rawName)
    {
        var formatted = NameFormatter.FormatMethodDisplayName(rawName);
        return GetCallTreeDisplayName(formatted);
    }

    public static string GetCallTreeMatchName(CallTreeNode node)
    {
        return NameFormatter.FormatMethodDisplayName(node.Name);
    }

    public static string GetCallTreeDisplayName(string matchName)
    {
        if (IsUnmanagedFrame(matchName))
        {
            return "Unmanaged Code";
        }

        return matchName;
    }

    public static string BuildJitMethodFilter(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        var trimmed = rawName.Trim();
        var paramIndex = trimmed.IndexOf('(');
        if (paramIndex >= 0)
        {
            trimmed = trimmed[..paramIndex];
        }

        trimmed = trimmed.Trim();
        trimmed = trimmed.Replace('+', '.');

        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot > 0 && lastDot < trimmed.Length - 1)
        {
            return $"{trimmed[..lastDot]}:{trimmed[(lastDot + 1)..]}";
        }

        return trimmed;
    }

    public static bool ShouldStopAtLeaf(string matchName)
    {
        return IsUnmanagedFrame(matchName) ||
               matchName.Contains("CastHelpers.", StringComparison.Ordinal) ||
               matchName.Contains("Array.Copy", StringComparison.Ordinal) ||
               matchName.Contains("Dictionary<__Canon,__Canon>.Resize", StringComparison.Ordinal) ||
               matchName.Contains("Buffer.BulkMoveWithWriteBarrier", StringComparison.Ordinal) ||
               matchName.Contains("SpanHelpers.SequenceEqual", StringComparison.Ordinal) ||
               matchName.Contains("HashSet<", StringComparison.Ordinal) ||
               matchName.Contains("Enumerable+ArrayWhereSelectIterator<", StringComparison.Ordinal) ||
               matchName.Contains("ImmutableDictionary<", StringComparison.Ordinal) ||
               matchName.Contains("SegmentedArrayBuilder<__Canon>.ToArray", StringComparison.Ordinal) ||
               matchName.Contains("__Canon", StringComparison.Ordinal) ||
               (matchName.Contains("List<", StringComparison.Ordinal) &&
                matchName.EndsWith(".ToArray", StringComparison.Ordinal));
    }

    public static bool IsRuntimeNoise(string name)
    {
        var trimmed = name.TrimStart();
        var formatted = FormatFunctionDisplayName(trimmed);
        return IsUnmanagedFrame(trimmed) ||
               trimmed.Contains("(Non-Activities)", StringComparison.Ordinal) ||
               trimmed.Contains("Thread", StringComparison.Ordinal) ||
               trimmed.Contains("Threads", StringComparison.Ordinal) ||
               trimmed.Contains("Process", StringComparison.Ordinal) ||
               FrameNameClassifier.StartsWithDigit(trimmed) ||
               FrameNameClassifier.StartsWithDigit(formatted);
    }
}
