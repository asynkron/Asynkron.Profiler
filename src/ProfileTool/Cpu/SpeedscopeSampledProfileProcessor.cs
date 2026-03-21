using System;
using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeSampledProfileProcessor
{
    public static void Process(
        JsonElement samplesElement,
        JsonElement? weightsElement,
        SpeedscopeAggregationState state,
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

            var weight = GetWeight(hasWeights, weightsArray, sampleIndex);
            var timeWeight = weight * timeScale;
            var callWeight = GetCallWeight(weight, isSampleUnit);

            var current = state.Root;
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
                current = AddFrameToPath(current, frameIdx, state, timeWeight, callWeight);
            }

            if (hasFrame)
            {
                current.Self += timeWeight;
                state.AddRootDuration(timeWeight);
            }

            sampleIndex++;
        }
    }

    private static double GetWeight(bool hasWeights, JsonElement weightsArray, int sampleIndex)
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

    private static CallTreeNode AddFrameToPath(
        CallTreeNode parent,
        int frameIdx,
        SpeedscopeAggregationState state,
        double timeWeight,
        int callWeight)
    {
        var child = state.GetOrCreateChild(parent, frameIdx);
        child.Total += timeWeight;
        child.Calls += callWeight;
        state.AddFrameTime(frameIdx, timeWeight);
        state.AddFrameCalls(frameIdx, callWeight);
        return child;
    }
}
