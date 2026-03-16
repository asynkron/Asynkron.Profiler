using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class CpuTraceAnalyzer
{
    public static CpuProfileResult Analyze(string traceFile, string outputDirectory)
    {
        var frameTotals = new Dictionary<string, double>(StringComparer.Ordinal);
        var frameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var frameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var framesList = new List<string>();
        var callTreeRoot = new CallTreeNode(-1, "Total");
        var callTreeTotal = 0d;
        var totalSamples = 0L;
        double? lastSampleTimeMs = null;
        var sawTypedException = false;

        var etlxPath = TraceLogFileResolver.Resolve(traceFile, outputDirectory);
        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();

        const string sampleProfilerProvider = "Microsoft-DotNETCore-SampleProfiler";

        void RecordException(TraceEvent data)
        {
            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var typeName = TraceEventPayloadReader.GetExceptionTypeName(data);
            callTreeRoot.AddExceptionTotals(1);
            CallTreeFrameWalker.VisitFrames(
                TraceCallStackFrameEnumerator.EnumerateCpuFrames(stack),
                callTreeRoot,
                frameIndices,
                framesList,
                (child, _, isLeaf) =>
                {
                    if (isLeaf)
                    {
                        child.AddException(typeName, 1);
                        return;
                    }

                    child.AddExceptionTotals(1);
                });
        }

        source.Clr.GCAllocationTick += data =>
        {
            var bytes = data.AllocationAmount64;
            if (bytes <= 0)
            {
                return;
            }

            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var typeName = string.IsNullOrWhiteSpace(data.TypeName) ? "Unknown" : data.TypeName;
            callTreeRoot.AddAllocationTotals(bytes);
            CallTreeFrameWalker.VisitFrames(
                TraceCallStackFrameEnumerator.EnumerateCpuFrames(stack),
                callTreeRoot,
                frameIndices,
                framesList,
                (child, _, isLeaf) =>
                {
                    if (isLeaf)
                    {
                        child.AddAllocation(typeName, bytes);
                        return;
                    }

                    child.AddAllocationTotals(bytes);
                });
        };

        source.Clr.ExceptionStart += data =>
        {
            sawTypedException = true;
            RecordException(data);
        };

        void RegisterFallbackExceptionEvent(string eventName)
        {
            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                eventName,
                data =>
                {
                    if (!sawTypedException)
                    {
                        RecordException(data);
                    }
                });
        }

        RegisterFallbackExceptionEvent("ExceptionStart");
        RegisterFallbackExceptionEvent("ExceptionThrown");

        source.Dynamic.All += data =>
        {
            if (!string.Equals(data.ProviderName, sampleProfilerProvider, StringComparison.Ordinal))
            {
                return;
            }

            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var timeMs = data.TimeStampRelativeMSec;
            var weight = 0d;
            if (lastSampleTimeMs.HasValue)
            {
                weight = timeMs - lastSampleTimeMs.Value;
                if (weight < 0)
                {
                    weight = 0;
                }
            }

            lastSampleTimeMs = timeMs;

            totalSamples++;
            callTreeTotal += weight;

            var node = CallTreeFrameWalker.VisitFrames(
                TraceCallStackFrameEnumerator.EnumerateCpuFrames(stack),
                callTreeRoot,
                frameIndices,
                framesList,
                (child, frame, _) =>
                {
                    if (weight > 0)
                    {
                        child.Total += weight;
                    }

                    child.IncrementCalls();

                    frameCounts.TryGetValue(frame, out var count);
                    frameCounts[frame] = count + 1;

                    frameTotals.TryGetValue(frame, out var total);
                    frameTotals[frame] = total + weight;
                });

            if (weight > 0)
            {
                node.Self += weight;
            }
        };

        source.Process();

        if (totalSamples == 0)
        {
            throw new ProfilerAnalysisException("No CPU samples found in trace.");
        }

        callTreeRoot.Total = callTreeTotal;
        callTreeRoot.Calls = totalSamples > int.MaxValue ? int.MaxValue : (int)totalSamples;

        var allFunctions = FunctionSampleBuilder.CreateSorted(frameTotals, frameCounts, frameIndices);
        var totalTime = frameTotals.Values.Sum();

        return new CpuProfileResult(
            allFunctions,
            totalTime,
            callTreeRoot,
            callTreeTotal,
            traceFile,
            "ms",
            "Samples",
            " samp");
    }
}
