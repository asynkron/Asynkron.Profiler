using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class CpuTraceAnalyzer
{
    public static CpuProfileResult Analyze(string etlxPath, string traceFile)
    {
        try
        {
            var state = new CpuTraceState();
            using var session = TraceLogSession.Open(etlxPath);

            session.Source.Clr.GCAllocationTick += data =>
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
                state.CallTreeRoot.AddAllocationTotals(bytes);
                TraceCallTreeBuilder.VisitCallTreeFrames(
                    TraceCallStackFrameEnumerator.EnumerateCpuFrames(stack),
                    state.CallTreeRoot,
                    state.FrameIndices,
                    state.FramesList,
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

            session.Source.Clr.ExceptionStart += data =>
            {
                state.SawTypedException = true;
                RecordException(data, state);
            };

            session.Source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionStart",
                data =>
                {
                    if (!state.SawTypedException)
                    {
                        RecordException(data, state);
                    }
                });

            session.Source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionThrown",
                data =>
                {
                    if (!state.SawTypedException)
                    {
                        RecordException(data, state);
                    }
                });

            session.Source.Dynamic.All += data =>
            {
                if (!string.Equals(data.ProviderName, "Microsoft-DotNETCore-SampleProfiler", StringComparison.Ordinal))
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
                if (state.LastSampleTimeMs.HasValue)
                {
                    weight = timeMs - state.LastSampleTimeMs.Value;
                    if (weight < 0)
                    {
                        weight = 0;
                    }
                }

                state.LastSampleTimeMs = timeMs;
                state.TotalSamples++;
                state.CallTreeTotal += weight;

                var node = TraceCallTreeBuilder.VisitCallTreeFrames(
                    TraceCallStackFrameEnumerator.EnumerateCpuFrames(stack),
                    state.CallTreeRoot,
                    state.FrameIndices,
                    state.FramesList,
                    (child, frame, _) =>
                    {
                        if (weight > 0)
                        {
                            child.Total += weight;
                        }

                        child.IncrementCalls();

                        state.FrameCounts.TryGetValue(frame, out var count);
                        state.FrameCounts[frame] = count + 1;

                        state.FrameTotals.TryGetValue(frame, out var total);
                        state.FrameTotals[frame] = total + weight;
                    });

                if (weight > 0)
                {
                    node.Self += weight;
                }
            };

            session.Source.Process();

            if (state.TotalSamples == 0)
            {
                throw new ProfilerAnalysisException("No CPU samples found in trace.");
            }

            state.CallTreeRoot.Total = state.CallTreeTotal;
            state.CallTreeRoot.Calls = state.TotalSamples > int.MaxValue ? int.MaxValue : (int)state.TotalSamples;

            var allFunctions = FunctionSampleBuilder.CreateSorted(state.FrameTotals, state.FrameCounts, state.FrameIndices);
            var totalTime = state.FrameTotals.Values.Sum();
            return CpuProfileResult.CreateTraceResult(allFunctions, totalTime, state.CallTreeRoot, state.CallTreeTotal, traceFile);
        }
        catch (ProfilerAnalysisException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"CPU trace parse failed: {ex.Message}", ex);
        }
    }

    private static void RecordException(TraceEvent data, CpuTraceState state)
    {
        var stack = data.CallStack();
        if (stack == null)
        {
            return;
        }

        var typeName = TraceEventPayloadReader.GetExceptionTypeName(data);
        state.CallTreeRoot.AddExceptionTotals(1);
        TraceCallTreeBuilder.VisitCallTreeFrames(
            TraceCallStackFrameEnumerator.EnumerateCpuFrames(stack),
            state.CallTreeRoot,
            state.FrameIndices,
            state.FramesList,
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

    private sealed class CpuTraceState
    {
        public Dictionary<string, double> FrameTotals { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FrameCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> FrameIndices { get; } = new(StringComparer.Ordinal);
        public List<string> FramesList { get; } = new();
        public CallTreeNode CallTreeRoot { get; } = CallTreeNode.CreateRoot();
        public long TotalSamples { get; set; }
        public double CallTreeTotal { get; set; }
        public double? LastSampleTimeMs { get; set; }
        public bool SawTypedException { get; set; }
    }
}
