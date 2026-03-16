using System.Collections.Generic;
using System.Linq;

namespace Asynkron.Profiler;

internal static class FunctionSampleBuilder
{
    public static List<FunctionSample> Build(
        IReadOnlyDictionary<string, double> frameTotals,
        IReadOnlyDictionary<string, int> frameCounts,
        IReadOnlyDictionary<string, int> frameIndices)
    {
        return frameTotals
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                frameCounts.TryGetValue(kv.Key, out var calls);
                frameIndices.TryGetValue(kv.Key, out var frameIndex);
                return new FunctionSample(kv.Key, kv.Value, calls, frameIndex);
            })
            .ToList();
    }
}
