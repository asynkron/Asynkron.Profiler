using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Asynkron.Profiler;

public static class SpeedscopeParser
{
    public static CpuProfileResult? ParseFile(string speedscopePath)
    {
        var json = File.ReadAllText(speedscopePath);
        return ParseJson(json, speedscopePath);
    }

    public static CpuProfileResult? ParseJson(string speedscopeJson, string? speedscopePath = null)
    {
        using var doc = JsonDocument.Parse(speedscopeJson);
        return ParseDocument(doc, speedscopePath);
    }

    private static CpuProfileResult? ParseDocument(JsonDocument doc, string? speedscopePath)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("shared", out var shared) ||
            !shared.TryGetProperty("frames", out var framesElement) ||
            framesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (!root.TryGetProperty("profiles", out var profilesElement) ||
            profilesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var framesList = new List<string>();
        foreach (var frame in framesElement.EnumerateArray())
        {
            var name = frame.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            framesList.Add(string.IsNullOrWhiteSpace(name) ? "Unknown" : name);
        }

        var frameTimes = new Dictionary<int, double>();
        var frameSelfTimes = new Dictionary<int, double>();
        var frameCounts = new Dictionary<int, int>();
        var callTreeRoot = new CallTreeNode(-1, "Total");
        var callTreeTotal = 0d;

        var hasSampledProfile = false;
        var hasSampleUnit = false;
        var hasTimeUnit = false;

        var parsedProfiles = 0;
        foreach (var profile in profilesElement.EnumerateArray())
        {
            var (timeScale, isSampleUnit) = GetUnitScale(profile);

            if (profile.TryGetProperty("events", out var eventsElement) &&
                eventsElement.ValueKind == JsonValueKind.Array)
            {
                parsedProfiles++;
                hasTimeUnit = true;
                ProcessEventedProfile(
                    eventsElement,
                    framesList,
                    callTreeRoot,
                    frameTimes,
                    frameSelfTimes,
                    frameCounts,
                    ref callTreeTotal,
                    timeScale);
                continue;
            }

            if (profile.TryGetProperty("samples", out var samplesElement) &&
                samplesElement.ValueKind == JsonValueKind.Array)
            {
                JsonElement? weightsElement = null;
                if (profile.TryGetProperty("weights", out var weightsValue) &&
                    weightsValue.ValueKind == JsonValueKind.Array)
                {
                    weightsElement = weightsValue;
                }

                parsedProfiles++;
                hasSampledProfile = true;
                if (isSampleUnit)
                {
                    hasSampleUnit = true;
                }
                else
                {
                    hasTimeUnit = true;
                }
                ProcessSampledProfile(
                    samplesElement,
                    weightsElement,
                    framesList,
                    callTreeRoot,
                    frameTimes,
                    frameCounts,
                    ref callTreeTotal,
                    timeScale,
                    isSampleUnit);
            }
        }

        if (parsedProfiles == 0)
        {
            return null;
        }

        var allFunctions = new List<FunctionSample>();
        var totalTime = frameTimes.Values.Sum();

        if (callTreeTotal <= 0)
        {
            callTreeTotal = SumCallTreeTotals(callTreeRoot);
        }

        callTreeRoot.Total = callTreeTotal;
        callTreeRoot.Calls = SumCallTreeCalls(callTreeRoot);

        foreach (var child in callTreeRoot.Children.Values)
        {
            if (child.HasTiming)
            {
                callTreeRoot.UpdateTiming(child.MinStart, child.MaxEnd);
            }
        }

        foreach (var (frameIdx, timeSpent) in frameTimes.OrderByDescending(kv => kv.Value))
        {
            var name = frameIdx < framesList.Count ? framesList[frameIdx] : "Unknown";
            frameCounts.TryGetValue(frameIdx, out var calls);
            allFunctions.Add(new FunctionSample(name, timeSpent, calls, frameIdx));
        }

        var timeUnitLabel = hasSampleUnit && !hasTimeUnit ? "samples" : "ms";
        var countLabel = hasSampledProfile ? "Samples" : "Calls";
        var countSuffix = hasSampledProfile ? " samp" : "x";

        return new CpuProfileResult(
            allFunctions,
            totalTime,
            callTreeRoot,
            callTreeTotal,
            speedscopePath,
            timeUnitLabel,
            countLabel,
            countSuffix);
    }

    private static void ProcessEventedProfile(
        JsonElement eventsElement,
        IReadOnlyList<string> framesList,
        CallTreeNode callTreeRoot,
        Dictionary<int, double> frameTimes,
        Dictionary<int, double> frameSelfTimes,
        Dictionary<int, int> frameCounts,
        ref double callTreeTotal,
        double timeScale)
    {
        var stack = new List<(CallTreeNode Node, double Start, int FrameIdx)>();
        var hasLast = false;
        var lastAt = 0d;

        foreach (var evt in eventsElement.EnumerateArray())
        {
            if (!evt.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!evt.TryGetProperty("frame", out var frameElement) ||
                frameElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (!evt.TryGetProperty("at", out var atElement) ||
                atElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var eventType = typeElement.GetString();
            var frameIdx = frameElement.GetInt32();
            var at = atElement.GetDouble() * timeScale;

            if (hasLast && stack.Count > 0)
            {
                var topIdx = stack[^1].FrameIdx;
                frameSelfTimes.TryGetValue(topIdx, out var selfTime);
                var delta = at - lastAt;
                frameSelfTimes[topIdx] = selfTime + delta;
                stack[^1].Node.Self += delta;
            }

            hasLast = true;
            lastAt = at;

            if (string.Equals(eventType, "O", StringComparison.Ordinal))
            {
                var parentNode = stack.Count > 0 ? stack[^1].Node : callTreeRoot;
                var childNode = GetOrCreateCallTreeChild(parentNode, frameIdx, framesList);
                childNode.Calls += 1;
                stack.Add((childNode, at, frameIdx));
                frameCounts.TryGetValue(frameIdx, out var count);
                frameCounts[frameIdx] = count + 1;
            }
            else if (string.Equals(eventType, "C", StringComparison.Ordinal))
            {
                if (stack.Count > 0 && stack[^1].FrameIdx == frameIdx)
                {
                    var (node, openTime, _) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    var duration = at - openTime;
                    frameTimes.TryGetValue(frameIdx, out var time);
                    frameTimes[frameIdx] = time + duration;
                    node.Total += duration;
                    node.UpdateTiming(openTime, at);
                    if (stack.Count == 0)
                    {
                        callTreeTotal += duration;
                    }
                }
            }
        }
    }

    private static void ProcessSampledProfile(
        JsonElement samplesElement,
        JsonElement? weightsElement,
        IReadOnlyList<string> framesList,
        CallTreeNode callTreeRoot,
        Dictionary<int, double> frameTimes,
        Dictionary<int, int> frameCounts,
        ref double callTreeTotal,
        double timeScale,
        bool isSampleUnit)
    {
        var hasWeights = weightsElement.HasValue &&
                         weightsElement.Value.ValueKind == JsonValueKind.Array;
        var weightsArray = weightsElement.GetValueOrDefault();
        var sampleIndex = 0;

        foreach (var sample in samplesElement.EnumerateArray())
        {
            if (sample.ValueKind != JsonValueKind.Array)
            {
                sampleIndex++;
                continue;
            }

            var weight = 1d;
            if (hasWeights && sampleIndex < weightsArray.GetArrayLength())
            {
                var weightElement = weightsArray[sampleIndex];
                if (weightElement.ValueKind == JsonValueKind.Number)
                {
                    weight = weightElement.GetDouble();
                }
            }

            var timeWeight = weight * timeScale;
            var callWeight = 1;
            if (isSampleUnit)
            {
                callWeight = weight <= 0
                    ? 0
                    : Math.Max(1, (int)Math.Round(weight));
            }

            var current = callTreeRoot;
            var hasFrame = false;
            foreach (var frameIdxElement in sample.EnumerateArray())
            {
                if (frameIdxElement.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var frameIdx = frameIdxElement.GetInt32();
                if (frameIdx < 0)
                {
                    continue;
                }

                hasFrame = true;
                var child = GetOrCreateCallTreeChild(current, frameIdx, framesList);
                child.Total += timeWeight;
                child.Calls += callWeight;
                frameTimes.TryGetValue(frameIdx, out var time);
                frameTimes[frameIdx] = time + timeWeight;
                frameCounts.TryGetValue(frameIdx, out var count);
                frameCounts[frameIdx] = count + callWeight;
                current = child;
            }

            if (hasFrame)
            {
                current.Self += timeWeight;
                callTreeTotal += timeWeight;
            }

            sampleIndex++;
        }
    }

    private static (double TimeScale, bool IsSampleUnit) GetUnitScale(JsonElement profile)
    {
        if (!profile.TryGetProperty("unit", out var unitElement) ||
            unitElement.ValueKind != JsonValueKind.String)
        {
            return (1d, false);
        }

        var unit = unitElement.GetString()?.Trim().ToLowerInvariant();
        return unit switch
        {
            "nanoseconds" or "nanosecond" or "ns" => (1d / 1_000_000d, false),
            "microseconds" or "microsecond" or "us" => (1d / 1_000d, false),
            "milliseconds" or "millisecond" or "ms" => (1d, false),
            "seconds" or "second" or "s" => (1_000d, false),
            "samples" or "sample" => (1d, true),
            _ => (1d, false)
        };
    }

    private static CallTreeNode GetOrCreateCallTreeChild(
        CallTreeNode parent,
        int frameIdx,
        IReadOnlyList<string> frames)
    {
        if (!parent.Children.TryGetValue(frameIdx, out var child))
        {
            var name = frameIdx >= 0 && frameIdx < frames.Count ? frames[frameIdx] : "Unknown";
            child = new CallTreeNode(frameIdx, name);
            parent.Children[frameIdx] = child;
        }

        return child;
    }

    private static double SumCallTreeTotals(CallTreeNode node)
    {
        var sum = 0d;
        foreach (var child in node.Children.Values)
        {
            sum += child.Total;
        }
        return sum;
    }

    private static int SumCallTreeCalls(CallTreeNode node)
    {
        var sum = 0;
        foreach (var child in node.Children.Values)
        {
            sum += child.Calls;
        }
        return sum;
    }
}
