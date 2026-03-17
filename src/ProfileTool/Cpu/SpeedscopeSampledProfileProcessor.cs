using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeSampledProfileProcessor
{
    public static void Process(
        JsonElement samplesElement,
        JsonElement? weightsElement,
        IReadOnlyList<string> framesList,
        SpeedscopeParseState state,
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

            var weight = ResolveWeight(weightsArray, hasWeights, sampleIndex);
            var timeWeight = weight * timeScale;
            var callWeight = ResolveCallWeight(weight, isSampleUnit);

            var current = state.CallTreeRoot;
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
                var child = SpeedscopeParseState.GetOrCreateCallTreeChild(current, frameIdx, framesList);
                child.Total += timeWeight;
                child.Calls += callWeight;
                state.FrameTimes.TryGetValue(frameIdx, out var time);
                state.FrameTimes[frameIdx] = time + timeWeight;
                state.FrameCounts.TryGetValue(frameIdx, out var count);
                state.FrameCounts[frameIdx] = count + callWeight;
                current = child;
            }

            if (hasFrame)
            {
                current.Self += timeWeight;
                state.CallTreeTotal += timeWeight;
            }

            sampleIndex++;
        }
    }

    private static int ResolveCallWeight(double weight, bool isSampleUnit)
    {
        if (!isSampleUnit)
        {
            return 1;
        }

        return weight <= 0
            ? 0
            : Math.Max(1, (int)Math.Round(weight));
    }

    private static double ResolveWeight(JsonElement weightsArray, bool hasWeights, int sampleIndex)
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
}
