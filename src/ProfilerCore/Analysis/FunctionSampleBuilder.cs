using System.Collections.Generic;
using System.Linq;

namespace Asynkron.Profiler;

internal static class FunctionSampleBuilder
{
    public static IReadOnlyList<FunctionSample> CreateSorted(
        IReadOnlyDictionary<string, double> frameTotals,
        IReadOnlyDictionary<string, int> frameCounts,
        IReadOnlyDictionary<string, int> frameIndices)
    {
        return frameTotals
            .OrderByDescending(kv => kv.Value)
            .Select(kv => Create(kv, frameCounts, frameIndices))
            .ToList();
    }

    private static FunctionSample Create(
        KeyValuePair<string, double> totalEntry,
        IReadOnlyDictionary<string, int> frameCounts,
        IReadOnlyDictionary<string, int> frameIndices)
    {
        frameCounts.TryGetValue(totalEntry.Key, out var calls);
        frameIndices.TryGetValue(totalEntry.Key, out var frameIdx);
        return new FunctionSample(totalEntry.Key, totalEntry.Value, calls, frameIdx);
    }
}
