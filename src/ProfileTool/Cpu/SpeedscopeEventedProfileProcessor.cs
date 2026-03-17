using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Asynkron.Profiler;

internal static class SpeedscopeEventedProfileProcessor
{
    public static void Process(
        JsonElement eventsElement,
        IReadOnlyList<string> framesList,
        SpeedscopeParseState state,
        double timeScale)
    {
        var stack = new List<(CallTreeNode Node, double Start, int FrameIdx)>();
        var hasLast = false;
        var lastAt = 0d;

        foreach (var evt in eventsElement.EnumerateArray())
        {
            if (!TryReadEvent(evt, out var eventType, out var frameIdx, out var at, timeScale))
            {
                continue;
            }

            if (hasLast && stack.Count > 0)
            {
                var topIdx = stack[^1].FrameIdx;
                state.FrameSelfTimes.TryGetValue(topIdx, out var selfTime);
                var delta = at - lastAt;
                state.FrameSelfTimes[topIdx] = selfTime + delta;
                stack[^1].Node.Self += delta;
            }

            hasLast = true;
            lastAt = at;

            if (string.Equals(eventType, "O", StringComparison.Ordinal))
            {
                var parentNode = stack.Count > 0 ? stack[^1].Node : state.CallTreeRoot;
                var childNode = SpeedscopeParseState.GetOrCreateCallTreeChild(parentNode, frameIdx, framesList);
                childNode.Calls += 1;
                stack.Add((childNode, at, frameIdx));
                state.FrameCounts.TryGetValue(frameIdx, out var count);
                state.FrameCounts[frameIdx] = count + 1;
            }
            else if (string.Equals(eventType, "C", StringComparison.Ordinal) &&
                     stack.Count > 0 &&
                     stack[^1].FrameIdx == frameIdx)
            {
                var (node, openTime, _) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                var duration = at - openTime;
                state.FrameTimes.TryGetValue(frameIdx, out var time);
                state.FrameTimes[frameIdx] = time + duration;
                node.Total += duration;
                node.UpdateTiming(openTime, at);
                if (stack.Count == 0)
                {
                    state.CallTreeTotal += duration;
                }
            }
        }
    }

    private static bool TryReadEvent(
        JsonElement evt,
        out string? eventType,
        out int frameIdx,
        out double at,
        double timeScale)
    {
        eventType = null;
        frameIdx = default;
        at = default;

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
        at = atElement.GetDouble() * timeScale;
        return true;
    }
}
