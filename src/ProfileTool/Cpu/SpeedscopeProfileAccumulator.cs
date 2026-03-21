using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Asynkron.Profiler;

internal sealed class SpeedscopeProfileAccumulator
{
    private readonly IReadOnlyList<string> _frames;
    private readonly Dictionary<int, double> _frameTimes = [];
    private readonly Dictionary<int, int> _frameCounts = [];
    private readonly CallTreeNode _callTreeRoot = new(-1, "Total");

    private double _callTreeTotal;
    private bool _hasSampledProfile;
    private bool _hasSampleUnit;
    private bool _hasTimeUnit;
    private int _parsedProfiles;

    public SpeedscopeProfileAccumulator(IReadOnlyList<string> frames)
    {
        _frames = frames;
    }

    public void AddProfile(JsonElement profile)
    {
        var (timeScale, isSampleUnit) = GetUnitScale(profile);
        if (SpeedscopeJsonElementHelper.TryGetArray(profile, "events", out var eventsElement))
        {
            _parsedProfiles++;
            _hasTimeUnit = true;
            ProcessEventedProfile(eventsElement, timeScale);
            return;
        }

        if (SpeedscopeJsonElementHelper.TryGetArray(profile, "samples", out var samplesElement))
        {
            _parsedProfiles++;
            _hasSampledProfile = true;
            if (isSampleUnit)
            {
                _hasSampleUnit = true;
            }
            else
            {
                _hasTimeUnit = true;
            }

            ProcessSampledProfile(
                samplesElement,
                SpeedscopeJsonElementHelper.GetOptionalArray(profile, "weights"),
                timeScale,
                isSampleUnit);
        }
    }

    public CpuProfileResult? BuildResult(string? speedscopePath)
    {
        if (_parsedProfiles == 0)
        {
            return null;
        }

        var totalTime = _frameTimes.Values.Sum();
        if (_callTreeTotal <= 0)
        {
            _callTreeTotal = SumNodeTotals(_callTreeRoot);
        }

        _callTreeRoot.Total = _callTreeTotal;
        _callTreeRoot.Calls = SumNodeCalls(_callTreeRoot);
        UpdateRootTiming();

        var timeUnitLabel = _hasSampleUnit && !_hasTimeUnit ? "samples" : "ms";
        var countLabel = _hasSampledProfile ? "Samples" : "Calls";
        var countSuffix = _hasSampledProfile ? " samp" : "x";

        return new CpuProfileResult(
            BuildFunctionSamples(),
            totalTime,
            _callTreeRoot,
            _callTreeTotal,
            speedscopePath,
            timeUnitLabel,
            countLabel,
            countSuffix);
    }

    private void ProcessEventedProfile(JsonElement eventsElement, double timeScale)
    {
        var stack = new List<(CallTreeNode Node, double Start, int FrameIdx)>();
        var hasLast = false;
        var lastAt = 0d;

        foreach (var evt in eventsElement.EnumerateArray())
        {
            if (!TryReadEvent(evt, out var eventType, out var frameIdx, out var at, timeScale))
            {
                continue;
            }

            if (hasLast && stack.Count > 0)
            {
                var delta = at - lastAt;
                stack[^1].Node.Self += delta;
            }

            hasLast = true;
            lastAt = at;

            if (string.Equals(eventType, "O", StringComparison.Ordinal))
            {
                var parentNode = stack.Count > 0 ? stack[^1].Node : _callTreeRoot;
                var childNode = GetOrCreateCallTreeChild(parentNode, frameIdx);
                childNode.Calls += 1;
                _frameCounts[frameIdx] = _frameCounts.GetValueOrDefault(frameIdx) + 1;
                stack.Add((childNode, at, frameIdx));
                continue;
            }

            if (string.Equals(eventType, "C", StringComparison.Ordinal) &&
                stack.Count > 0 &&
                stack[^1].FrameIdx == frameIdx)
            {
                var (node, openTime, _) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                var duration = at - openTime;
                _frameTimes[frameIdx] = _frameTimes.GetValueOrDefault(frameIdx) + duration;
                node.Total += duration;
                node.UpdateTiming(openTime, at);
                if (stack.Count == 0)
                {
                    _callTreeTotal += duration;
                }
            }
        }
    }

    private void ProcessSampledProfile(
        JsonElement samplesElement,
        JsonElement? weightsElement,
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

            var weight = GetSampleWeight(weightsArray, hasWeights, sampleIndex);
            var timeWeight = weight * timeScale;
            var callWeight = GetCallWeight(weight, isSampleUnit);

            var current = _callTreeRoot;
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
                var child = GetOrCreateCallTreeChild(current, frameIdx);
                child.Total += timeWeight;
                child.Calls += callWeight;
                _frameTimes[frameIdx] = _frameTimes.GetValueOrDefault(frameIdx) + timeWeight;
                _frameCounts[frameIdx] = _frameCounts.GetValueOrDefault(frameIdx) + callWeight;
                current = child;
            }

            if (hasFrame)
            {
                current.Self += timeWeight;
                _callTreeTotal += timeWeight;
            }

            sampleIndex++;
        }
    }

    private List<FunctionSample> BuildFunctionSamples()
    {
        var allFunctions = new List<FunctionSample>();
        foreach (var (frameIdx, timeSpent) in _frameTimes.OrderByDescending(entry => entry.Value))
        {
            var name = frameIdx < _frames.Count ? _frames[frameIdx] : "Unknown";
            _frameCounts.TryGetValue(frameIdx, out var calls);
            allFunctions.Add(new FunctionSample(name, timeSpent, calls, frameIdx));
        }

        return allFunctions;
    }

    private void UpdateRootTiming()
    {
        foreach (var child in _callTreeRoot.Children.Values)
        {
            if (child.HasTiming)
            {
                _callTreeRoot.UpdateTiming(child.MinStart, child.MaxEnd);
            }
        }
    }

    private CallTreeNode GetOrCreateCallTreeChild(CallTreeNode parent, int frameIdx)
    {
        if (!parent.Children.TryGetValue(frameIdx, out var child))
        {
            var name = frameIdx >= 0 && frameIdx < _frames.Count ? _frames[frameIdx] : "Unknown";
            child = new CallTreeNode(frameIdx, name);
            parent.Children[frameIdx] = child;
        }

        return child;
    }

    private static bool TryReadEvent(
        JsonElement evt,
        out string? eventType,
        out int frameIdx,
        out double at,
        double timeScale)
    {
        eventType = null;
        frameIdx = 0;
        at = 0d;

        if (!evt.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            !evt.TryGetProperty("frame", out var frameElement) ||
            frameElement.ValueKind != JsonValueKind.Number ||
            !evt.TryGetProperty("at", out var atElement) ||
            atElement.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        eventType = typeElement.GetString();
        frameIdx = frameElement.GetInt32();
        at = atElement.GetDouble() * timeScale;
        return true;
    }

    private static double GetSampleWeight(JsonElement weightsArray, bool hasWeights, int sampleIndex)
    {
        if (!hasWeights || sampleIndex >= weightsArray.GetArrayLength())
        {
            return 1d;
        }

        var weightElement = weightsArray[sampleIndex];
        return weightElement.ValueKind == JsonValueKind.Number
            ? weightElement.GetDouble()
            : 1d;
    }

    private static int GetCallWeight(double weight, bool isSampleUnit)
    {
        if (!isSampleUnit)
        {
            return 1;
        }

        return weight <= 0
            ? 0
            : Math.Max(1, (int)Math.Round(weight));
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

    private static double SumNodeTotals(CallTreeNode node)
    {
        var sum = 0d;
        foreach (var child in node.Children.Values)
        {
            sum += child.Total;
        }

        return sum;
    }

    private static int SumNodeCalls(CallTreeNode node)
    {
        var sum = 0;
        foreach (var child in node.Children.Values)
        {
            sum += child.Calls;
        }

        return sum;
    }
}
