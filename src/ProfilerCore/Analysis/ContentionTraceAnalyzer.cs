using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Asynkron.Profiler;

internal static class ContentionTraceAnalyzer
{
    public static ContentionProfileResult Analyze(string etlxPath)
    {
        try
        {
            var state = new ContentionTraceState();
            using var session = TraceLogSession.Open(etlxPath);

            session.Source.Clr.ContentionStart += data =>
            {
                state.SawTypedEvent = true;
                HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack(), state);
            };

            session.Source.Clr.ContentionStop += data =>
            {
                state.SawTypedEvent = true;
                var durationMs = data.DurationNs > 0
                    ? data.DurationNs / 1_000_000d
                    : 0d;
                HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack(), state);
            };

            session.Source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ContentionStart_V2",
                data => HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack(), state));

            session.Source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ContentionStop_V2",
                data => HandleStop(
                    data.ThreadID,
                    data.TimeStampRelativeMSec,
                    TraceEventPayloadReader.TryGetPayloadDurationMs(data),
                    data.CallStack(),
                    state));

            RegisterFallbackContentionEvent(
                session,
                "ContentionStart",
                state,
                data => HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack(), state));
            RegisterFallbackContentionEvent(
                session,
                "ContentionStop",
                state,
                data => HandleStop(
                    data.ThreadID,
                    data.TimeStampRelativeMSec,
                    TraceEventPayloadReader.TryGetPayloadDurationMs(data),
                    data.CallStack(),
                    state));

            session.Source.Process();

            state.CallTreeRoot.Total = state.TotalWaitMs;
            state.CallTreeRoot.Calls = state.TotalCount > int.MaxValue ? int.MaxValue : (int)state.TotalCount;

            var topFunctions = FunctionSampleBuilder.CreateSorted(state.FrameTotals, state.FrameCounts, state.FrameIndices);
            return new ContentionProfileResult(topFunctions, state.CallTreeRoot, state.TotalWaitMs, state.TotalCount);
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"Contention trace parse failed: {ex.Message}", ex);
        }
    }

    private static void RegisterFallbackContentionEvent(
        TraceLogSession session,
        string eventName,
        ContentionTraceState state,
        Action<TraceEvent> handler)
    {
        session.Source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            eventName,
            data =>
            {
                if (!state.SawTypedEvent)
                {
                    handler(data);
                }
            });
    }

    private static void HandleStart(int threadId, double timeMs, TraceCallStack? stack, ContentionTraceState state)
    {
        if (!state.Pending.TryGetValue(threadId, out var stackList))
        {
            stackList = new Stack<(double StartTime, TraceCallStack? Stack)>();
            state.Pending[threadId] = stackList;
        }

        stackList.Push((timeMs, stack));
    }

    private static void HandleStop(
        int threadId,
        double timeMs,
        double durationMs,
        TraceCallStack? stack,
        ContentionTraceState state)
    {
        if (state.Pending.TryGetValue(threadId, out var stackList) && stackList.Count > 0)
        {
            var entry = stackList.Pop();
            if (stackList.Count == 0)
            {
                state.Pending.Remove(threadId);
            }

            if (durationMs <= 0)
            {
                durationMs = timeMs - entry.StartTime;
            }

            stack ??= entry.Stack;
        }

        if (durationMs <= 0)
        {
            return;
        }

        TraceCallTreeBuilder.VisitCallTreeFrames(
            TraceCallStackFrameEnumerator.EnumerateContentionFrames(stack),
            state.CallTreeRoot,
            state.FrameIndices,
            state.FramesList,
            (child, frame, _) =>
            {
                child.Total += durationMs;
                child.IncrementCalls();

                state.FrameTotals[frame] = state.FrameTotals.TryGetValue(frame, out var total)
                    ? total + durationMs
                    : durationMs;
                state.FrameCounts[frame] = state.FrameCounts.TryGetValue(frame, out var count)
                    ? count + 1
                    : 1;
            });

        state.TotalWaitMs += durationMs;
        state.TotalCount++;
    }

    private sealed class ContentionTraceState
    {
        public Dictionary<string, double> FrameTotals { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FrameCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FrameIndices { get; } = new(StringComparer.Ordinal);
        public List<string> FramesList { get; } = new();
        public CallTreeNode CallTreeRoot { get; } = CallTreeNode.CreateRoot();
        public Dictionary<int, Stack<(double StartTime, TraceCallStack? Stack)>> Pending { get; } = new();
        public double TotalWaitMs { get; set; }
        public long TotalCount { get; set; }
        public bool SawTypedEvent { get; set; }
    }
}
