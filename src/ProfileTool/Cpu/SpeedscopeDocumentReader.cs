using System.Collections.Generic;
using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeDocumentReader
{
    public static bool TryRead(JsonDocument doc, out SpeedscopeDocument speedscopeDocument)
    {
        speedscopeDocument = default;

        var root = doc.RootElement;
        if (!root.TryGetProperty("shared", out var shared) ||
            !shared.TryGetProperty("frames", out var framesElement) ||
            framesElement.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("profiles", out var profilesElement) ||
            profilesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var frames = new List<string>();
        foreach (var frame in framesElement.EnumerateArray())
        {
            var name = frame.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            frames.Add(string.IsNullOrWhiteSpace(name) ? "Unknown" : name);
        }

        speedscopeDocument = new SpeedscopeDocument(frames, profilesElement);
        return true;
    }

    public static JsonElement? ReadWeights(JsonElement profile)
    {
        if (profile.TryGetProperty("weights", out var weightsElement) &&
            weightsElement.ValueKind == JsonValueKind.Array)
        {
            return weightsElement;
        }

        return null;
    }
}

internal readonly record struct SpeedscopeDocument(IReadOnlyList<string> Frames, JsonElement Profiles);
