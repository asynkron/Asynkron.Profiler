using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class ExceptionTraceAnalyzer
{
    public static ExceptionProfileResult Analyze(string etlxPath)
    {
        try
        {
            var state = new ExceptionTraceState();
            using var session = TraceLogSession.Open(etlxPath);

            session.Source.Clr.ExceptionStart += data =>
            {
                state.SawTypedThrow = true;
                RecordThrow(data, state);
            };

            session.Source.Clr.ExceptionCatchStart += data =>
            {
                state.SawTypedCatch = true;
                RecordCatch(data, state);
            };

            RegisterDynamicExceptionEvent(session, "ExceptionStart", () => state.SawTypedThrow, data => RecordThrow(data, state));
            RegisterDynamicExceptionEvent(session, "ExceptionThrown", () => state.SawTypedThrow, data => RecordThrow(data, state));
            RegisterDynamicExceptionEvent(session, "ExceptionCatchStart", () => state.SawTypedCatch, data => RecordCatch(data, state));

            session.Source.Process();

            state.ThrowRoot.Total = state.TotalThrown;
            state.ThrowRoot.Calls = state.TotalThrown > int.MaxValue ? int.MaxValue : (int)state.TotalThrown;

            CallTreeNode? catchRootResult = null;
            if (state.TotalCaught > 0)
            {
                state.CatchRoot.Total = state.TotalCaught;
                state.CatchRoot.Calls = state.TotalCaught > int.MaxValue ? int.MaxValue : (int)state.TotalCaught;
                catchRootResult = state.CatchRoot;
            }

            SetTypeRootTotals(state.TypeThrowCounts, state.TypeThrowRoots);
            SetTypeRootTotals(state.TypeCatchCounts, state.TypeCatchRoots);

            var typeDetails = BuildTypeDetails(state);
            var exceptionTypes = state.ExceptionCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new ExceptionTypeSample(kv.Key, kv.Value))
                .ToList();
            var catchSiteList = ExceptionSiteSampleBuilder.Create(state.CatchSites);

            return new ExceptionProfileResult(
                exceptionTypes,
                state.ThrowRoot,
                state.TotalThrown,
                typeDetails,
                catchSiteList,
                catchRootResult,
                state.TotalCaught);
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"Exception trace parse failed: {ex.Message}", ex);
        }
    }

    private static void RegisterDynamicExceptionEvent(
        TraceLogSession session,
        string eventName,
        Func<bool> hasTypedHandler,
        Action<TraceEvent> recorder)
    {
        session.Source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            eventName,
            data =>
            {
                if (!hasTypedHandler())
                {
                    recorder(data);
                }
            });
    }

    private static void RecordThrow(TraceEvent data, ExceptionTraceState state)
    {
        var typeName = TraceEventPayloadReader.GetExceptionTypeName(data);
        state.ExceptionCounts[typeName] = state.ExceptionCounts.TryGetValue(typeName, out var count)
            ? count + 1
            : 1;
        state.TotalThrown++;

        TraceCallTreeBuilder.RecordExceptionStack(
            data.CallStack(),
            state.ThrowRoot,
            state.ThrowFrameIndices,
            state.ThrowFramesList);

        state.TypeThrowCounts[typeName] = state.TypeThrowCounts.TryGetValue(typeName, out var typeCount)
            ? typeCount + 1
            : 1;

        var typeRoot = TraceCallTreeBuilder.GetOrCreateTypeRoot(state.TypeThrowRoots, typeName);
        TraceCallTreeBuilder.RecordExceptionStack(
            data.CallStack(),
            typeRoot,
            state.ThrowFrameIndices,
            state.ThrowFramesList);
    }

    private static void RecordCatch(TraceEvent data, ExceptionTraceState state)
    {
        state.TotalCaught++;
        TraceCallTreeBuilder.RecordExceptionStack(
            data.CallStack(),
            state.CatchRoot,
            state.CatchFrameIndices,
            state.CatchFramesList);

        var typeName = TraceEventPayloadReader.GetExceptionTypeName(data);
        state.TypeCatchCounts[typeName] = state.TypeCatchCounts.TryGetValue(typeName, out var typeCount)
            ? typeCount + 1
            : 1;

        var typeRoot = TraceCallTreeBuilder.GetOrCreateTypeRoot(state.TypeCatchRoots, typeName);
        TraceCallTreeBuilder.RecordExceptionStack(
            data.CallStack(),
            typeRoot,
            state.CatchFrameIndices,
            state.CatchFramesList);

        var catchSite = TraceCallStackFrameEnumerator.GetTopFrameName(data.CallStack()) ?? "Unknown";
        state.CatchSites[catchSite] = state.CatchSites.TryGetValue(catchSite, out var count)
            ? count + 1
            : 1;

        if (!state.TypeCatchSites.TryGetValue(typeName, out var typeSites))
        {
            typeSites = new Dictionary<string, long>(StringComparer.Ordinal);
            state.TypeCatchSites[typeName] = typeSites;
        }

        typeSites[catchSite] = typeSites.TryGetValue(catchSite, out var siteCount)
            ? siteCount + 1
            : 1;
    }

    private static void SetTypeRootTotals(
        Dictionary<string, long> typeCounts,
        Dictionary<string, CallTreeNode> typeRoots)
    {
        foreach (var (typeName, count) in typeCounts)
        {
            var typeRoot = TraceCallTreeBuilder.GetOrCreateTypeRoot(typeRoots, typeName);
            typeRoot.Total = count;
            typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
        }
    }

    private static Dictionary<string, ExceptionTypeDetails> BuildTypeDetails(ExceptionTraceState state)
    {
        var typeDetails = new Dictionary<string, ExceptionTypeDetails>(StringComparer.Ordinal);
        foreach (var (typeName, thrownCount) in state.TypeThrowCounts)
        {
            state.TypeCatchCounts.TryGetValue(typeName, out var caughtCount);
            state.TypeThrowRoots.TryGetValue(typeName, out var throwRootNode);
            state.TypeCatchRoots.TryGetValue(typeName, out var catchRootNode);
            state.TypeCatchSites.TryGetValue(typeName, out var sites);
            var siteList = ExceptionSiteSampleBuilder.Create(sites);

            if (throwRootNode != null)
            {
                typeDetails[typeName] = new ExceptionTypeDetails(
                    thrownCount,
                    throwRootNode,
                    caughtCount,
                    catchRootNode,
                    siteList);
            }
        }

        foreach (var (typeName, caughtCount) in state.TypeCatchCounts)
        {
            if (typeDetails.ContainsKey(typeName))
            {
                continue;
            }

            state.TypeCatchRoots.TryGetValue(typeName, out var catchRootNode);
            state.TypeCatchSites.TryGetValue(typeName, out var sites);
            var siteList = ExceptionSiteSampleBuilder.Create(sites);
            typeDetails[typeName] = new ExceptionTypeDetails(
                0,
                CallTreeNode.CreateRoot(),
                caughtCount,
                catchRootNode,
                siteList);
        }

        return typeDetails;
    }

    private sealed class ExceptionTraceState
    {
        public Dictionary<string, long> ExceptionCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CallTreeNode> TypeThrowRoots { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> TypeThrowCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CallTreeNode> TypeCatchRoots { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> TypeCatchCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, long>> TypeCatchSites { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> ThrowFrameIndices { get; } = new(StringComparer.Ordinal);
        public List<string> ThrowFramesList { get; } = new();
        public CallTreeNode ThrowRoot { get; } = CallTreeNode.CreateRoot();
        public Dictionary<string, int> CatchFrameIndices { get; } = new(StringComparer.Ordinal);
        public List<string> CatchFramesList { get; } = new();
        public CallTreeNode CatchRoot { get; } = CallTreeNode.CreateRoot();
        public Dictionary<string, long> CatchSites { get; } = new(StringComparer.Ordinal);
        public long TotalThrown { get; set; }
        public long TotalCaught { get; set; }
        public bool SawTypedThrow { get; set; }
        public bool SawTypedCatch { get; set; }
    }
}
