using System;
using System.Collections.Generic;
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

        var state = new SpeedscopeParseState();
        foreach (var profile in profilesElement.EnumerateArray())
        {
            var (timeScale, isSampleUnit) = GetUnitScale(profile);

            if (profile.TryGetProperty("events", out var eventsElement) &&
                eventsElement.ValueKind == JsonValueKind.Array)
            {
                state.ParsedProfiles++;
                state.HasTimeUnit = true;
                SpeedscopeEventedProfileProcessor.Process(
                    eventsElement,
                    framesList,
                    state,
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

                state.ParsedProfiles++;
                state.HasSampledProfile = true;
                if (isSampleUnit)
                {
                    state.HasSampleUnit = true;
                }
                else
                {
                    state.HasTimeUnit = true;
                }
                SpeedscopeSampledProfileProcessor.Process(
                    samplesElement,
                    weightsElement,
                    framesList,
                    state,
                    timeScale,
                    isSampleUnit);
            }
        }

        if (state.ParsedProfiles == 0)
        {
            return null;
        }

        return state.BuildResult(framesList, speedscopePath);
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
}
