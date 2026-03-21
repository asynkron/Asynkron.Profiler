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
            !SpeedscopeJsonElementHelper.TryGetArray(root, "profiles", out var profilesElement))
        {
            return null;
        }

        var accumulator = new SpeedscopeProfileAccumulator(frames);
        foreach (var profile in profilesElement.EnumerateArray())
        {
            accumulator.AddProfile(profile);
        }

        return accumulator.BuildResult(speedscopePath);
    }

    private static bool TryReadFrames(JsonElement root, out IReadOnlyList<string> frames)
    {
        frames = Array.Empty<string>();
        if (!root.TryGetProperty("shared", out var shared) ||
            !SpeedscopeJsonElementHelper.TryGetArray(shared, "frames", out var framesElement))
        {
            return false;
        }

        var names = new List<string>();
        foreach (var frame in framesElement.EnumerateArray())
        {
            var name = frame.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            names.Add(string.IsNullOrWhiteSpace(name) ? "Unknown" : name);
        }

        frames = names;
        return true;
    }
}
