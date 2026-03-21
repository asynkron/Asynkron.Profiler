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
        if (!TryReadFrames(root, out var frames) ||
            !TryGetProfiles(root, out var profilesElement))
        {
            return null;
        }

        var accumulator = new SpeedscopeProfileAccumulator(frames, speedscopePath);
        foreach (var profile in profilesElement.EnumerateArray())
        {
            accumulator.TryProcess(profile);
        }

        return accumulator.BuildResult();
    }

    private static bool TryReadFrames(JsonElement root, out IReadOnlyList<string> frames)
    {
        frames = Array.Empty<string>();
        if (!root.TryGetProperty("shared", out var shared) ||
            !shared.TryGetProperty("frames", out var framesElement) ||
            framesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var frameNames = new List<string>();
        foreach (var frame in framesElement.EnumerateArray())
        {
            var name = frame.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            frameNames.Add(string.IsNullOrWhiteSpace(name) ? "Unknown" : name);
        }

        frames = frameNames;
        return true;
    }

    private static bool TryGetProfiles(JsonElement root, out JsonElement profilesElement)
    {
        if (root.TryGetProperty("profiles", out profilesElement) &&
            profilesElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        profilesElement = default;
        return false;
    }
}
