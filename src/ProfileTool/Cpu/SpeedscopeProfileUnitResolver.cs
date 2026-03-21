using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeProfileUnitResolver
{
    public static (double TimeScale, bool IsSampleUnit) GetUnitScale(JsonElement profile)
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
}
