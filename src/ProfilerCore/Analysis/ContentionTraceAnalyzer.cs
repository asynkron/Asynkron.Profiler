using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class ContentionTraceAnalyzer
{
    public static ContentionProfileResult Analyze(string traceFile, string outputDirectory)
    {
        var frameTotals = new Dictionary<string, double>(StringComparer.Ordinal);
        var frameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var frameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var framesList = new List<string>();
        var callTreeRoot = new CallTreeNode(-1, "Total");
        var pending = new Dictionary<int, Stack<(double StartTime, TraceCallStack? Stack)>>();
        var totalWaitMs = 0d;
        long totalCount = 0;

        var etlxPath = TraceLogFileResolver.Resolve(traceFile, outputDirectory);
        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();

        var sawTypedEvent = false;

        void HandleStart(int threadId, double timeMs, TraceCallStack? stack)
        {
            if (!pending.TryGetValue(threadId, out var stackList))
            {
                stackList = new Stack<(double StartTime, TraceCallStack? Stack)>();
                pending[threadId] = stackList;
            }

            stackList.Push((timeMs, stack));
        }

        void HandleStop(int threadId, double timeMs, double durationMs, TraceCallStack? stack)
        {
            if (pending.TryGetValue(threadId, out var stackList) && stackList.Count > 0)
            {
                var entry = stackList.Pop();
                if (stackList.Count == 0)
                {
                    pending.Remove(threadId);
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

            CallTreeFrameWalker.VisitFrames(
                TraceCallStackFrameEnumerator.EnumerateContentionFrames(stack),
                callTreeRoot,
                frameIndices,
                framesList,
                (child, frame, _) =>
                {
                    child.Total += durationMs;
                    child.IncrementCalls();

                    frameTotals[frame] = frameTotals.TryGetValue(frame, out var total)
                        ? total + durationMs
                        : durationMs;
                    frameCounts[frame] = frameCounts.TryGetValue(frame, out var count)
                        ? count + 1
                        : 1;
                });

            totalWaitMs += durationMs;
            totalCount += 1;
        }

        void RecordDynamicStop(TraceEvent data)
        {
            var durationMs = TraceEventPayloadReader.TryGetPayloadDurationMs(data);
            HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
        }

        source.Clr.ContentionStart += data =>
        {
            sawTypedEvent = true;
            HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack());
        };

        source.Clr.ContentionStop += data =>
        {
            sawTypedEvent = true;
            var durationMs = data.DurationNs > 0
                ? data.DurationNs / 1_000_000d
                : 0d;
            HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
        };

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ContentionStart_V2",
            data => HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack()));

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ContentionStop_V2",
            RecordDynamicStop);

        void RegisterFallbackContentionEvent(string eventName, Action<TraceEvent> handler)
        {
            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                eventName,
                data =>
                {
                    if (!sawTypedEvent)
                    {
                        handler(data);
                    }
                });
        }

        RegisterFallbackContentionEvent(
            "ContentionStart",
            data => HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack()));
        RegisterFallbackContentionEvent("ContentionStop", RecordDynamicStop);

        source.Process();

        callTreeRoot.Total = totalWaitMs;
        callTreeRoot.Calls = totalCount > int.MaxValue ? int.MaxValue : (int)totalCount;

        var topFunctions = FunctionSampleBuilder.CreateSorted(frameTotals, frameCounts, frameIndices);
        return new ContentionProfileResult(topFunctions, callTreeRoot, totalWaitMs, totalCount);
    }
}
