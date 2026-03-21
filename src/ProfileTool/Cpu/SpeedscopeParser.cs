using System;
using System.IO;
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
        if (!SpeedscopeDocumentReader.TryRead(doc, out var speedscopeDocument))
        {
            return null;
        }

        var state = new SpeedscopeAggregationState(speedscopeDocument.Frames);
        foreach (var profile in speedscopeDocument.Profiles.EnumerateArray())
        {
            var (timeScale, isSampleUnit) = SpeedscopeProfileUnitResolver.GetUnitScale(profile);

            if (profile.TryGetProperty("events", out var eventsElement) &&
                eventsElement.ValueKind == JsonValueKind.Array)
            {
                state.RegisterEventedProfile();
                SpeedscopeEventedProfileProcessor.Process(eventsElement, state, timeScale);
                continue;
            }

            if (profile.TryGetProperty("samples", out var samplesElement) &&
                samplesElement.ValueKind == JsonValueKind.Array)
            {
                state.RegisterSampledProfile(isSampleUnit);
                SpeedscopeSampledProfileProcessor.Process(
                    samplesElement,
                    SpeedscopeDocumentReader.ReadWeights(profile),
                    state,
                    timeScale,
                    isSampleUnit);
            }
        }

        return state.BuildResult(speedscopePath);
    }
}
