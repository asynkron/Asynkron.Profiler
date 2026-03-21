using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeEventedProfileProcessor
{
    public static void Process(JsonElement eventsElement, SpeedscopeAggregationState state, double timeScale)
    {
        var stack = new List<FrameActivation>();
        var hasLastTimestamp = false;
        var lastTimestamp = 0d;

        foreach (var evt in eventsElement.EnumerateArray())
        {
            if (!TryReadEvent(evt, timeScale, out var eventType, out var frameIdx, out var timestamp))
            {
                continue;
            }

            if (hasLastTimestamp && stack.Count > 0)
            {
                stack[^1].Node.Self += timestamp - lastTimestamp;
            }

            hasLastTimestamp = true;
            lastTimestamp = timestamp;

            if (string.Equals(eventType, "O", StringComparison.Ordinal))
            {
                var parentNode = stack.Count > 0 ? stack[^1].Node : state.Root;
                var childNode = state.GetOrCreateChild(parentNode, frameIdx);
                childNode.IncrementCalls();
                state.AddFrameCalls(frameIdx, 1);
                stack.Add(new FrameActivation(childNode, timestamp, frameIdx));
            }
            else if (string.Equals(eventType, "C", StringComparison.Ordinal) &&
                     stack.Count > 0 &&
                     stack[^1].FrameIdx == frameIdx)
            {
                var (node, openTimestamp, _) = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                var duration = timestamp - openTimestamp;
                state.AddFrameTime(frameIdx, duration);
                node.Total += duration;
                node.UpdateTiming(openTimestamp, timestamp);

                if (stack.Count == 0)
                {
                    state.AddRootDuration(duration);
                }
            }
        }
    }

    private static bool TryReadEvent(
        JsonElement evt,
        double timeScale,
        out string? eventType,
        out int frameIdx,
        out double timestamp)
    {
        eventType = null;
        frameIdx = default;
        timestamp = default;

        if (!evt.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            !evt.TryGetProperty("frame", out var frameElement) ||
            frameElement.ValueKind != JsonValueKind.Number ||
            !evt.TryGetProperty("at", out var atElement) ||
            atElement.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        eventType = typeElement.GetString();
        frameIdx = frameElement.GetInt32();
        timestamp = atElement.GetDouble() * timeScale;
        return true;
    }

    private readonly record struct FrameActivation(CallTreeNode Node, double StartTime, int FrameIdx);
}
