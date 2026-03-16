using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class ExceptionTraceAnalyzer
{
    public static ExceptionProfileResult Analyze(string traceFile, string outputDirectory)
    {
        var exceptionCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var typeDetails = new Dictionary<string, ExceptionTypeDetails>(StringComparer.Ordinal);
        var typeThrowRoots = new Dictionary<string, CallTreeNode>(StringComparer.Ordinal);
        var typeThrowCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var typeCatchRoots = new Dictionary<string, CallTreeNode>(StringComparer.Ordinal);
        var typeCatchCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var typeCatchSites = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        var throwFrameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var throwFramesList = new List<string>();
        var throwRoot = new CallTreeNode(-1, "Total");
        var catchFrameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var catchFramesList = new List<string>();
        var catchRoot = new CallTreeNode(-1, "Total");
        var catchSites = new Dictionary<string, long>(StringComparer.Ordinal);
        long totalThrown = 0;
        long totalCaught = 0;

        var etlxPath = TraceLogFileResolver.Resolve(traceFile, outputDirectory);
        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();

        var sawTypedThrow = false;
        var sawTypedCatch = false;

        void RecordThrow(TraceEvent data)
        {
            var typeName = TraceEventPayloadReader.GetExceptionTypeName(data);
            exceptionCounts[typeName] = exceptionCounts.TryGetValue(typeName, out var count)
                ? count + 1
                : 1;
            totalThrown += 1;
            CallTreeFrameWalker.RecordExceptionStack(
                data.CallStack(),
                throwRoot,
                throwFrameIndices,
                throwFramesList);

            typeThrowCounts[typeName] = typeThrowCounts.TryGetValue(typeName, out var typeCount)
                ? typeCount + 1
                : 1;
            var typeRoot = CallTreeFrameWalker.GetOrCreateTypeRoot(typeThrowRoots, typeName);

            CallTreeFrameWalker.RecordExceptionStack(
                data.CallStack(),
                typeRoot,
                throwFrameIndices,
                throwFramesList);
        }

        void RecordCatch(TraceEvent data)
        {
            totalCaught += 1;
            CallTreeFrameWalker.RecordExceptionStack(
                data.CallStack(),
                catchRoot,
                catchFrameIndices,
                catchFramesList);

            var typeName = TraceEventPayloadReader.GetExceptionTypeName(data);
            typeCatchCounts[typeName] = typeCatchCounts.TryGetValue(typeName, out var typeCount)
                ? typeCount + 1
                : 1;
            var typeRoot = CallTreeFrameWalker.GetOrCreateTypeRoot(typeCatchRoots, typeName);

            CallTreeFrameWalker.RecordExceptionStack(
                data.CallStack(),
                typeRoot,
                catchFrameIndices,
                catchFramesList);

            var catchSite = TraceCallStackFrameEnumerator.GetTopFrameName(data.CallStack()) ?? "Unknown";
            catchSites[catchSite] = catchSites.TryGetValue(catchSite, out var count)
                ? count + 1
                : 1;

            if (!typeCatchSites.TryGetValue(typeName, out var typeSites))
            {
                typeSites = new Dictionary<string, long>(StringComparer.Ordinal);
                typeCatchSites[typeName] = typeSites;
            }

            typeSites[catchSite] = typeSites.TryGetValue(catchSite, out var siteCount)
                ? siteCount + 1
                : 1;
        }

        source.Clr.ExceptionStart += data =>
        {
            sawTypedThrow = true;
            RecordThrow(data);
        };

        source.Clr.ExceptionCatchStart += data =>
        {
            sawTypedCatch = true;
            RecordCatch(data);
        };

        void RegisterDynamicExceptionEvent(string eventName, Func<bool> hasTypedHandler, Action<TraceEvent> recorder)
        {
            source.Dynamic.AddCallbackForProviderEvent(
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

        RegisterDynamicExceptionEvent("ExceptionStart", () => sawTypedThrow, RecordThrow);
        RegisterDynamicExceptionEvent("ExceptionThrown", () => sawTypedThrow, RecordThrow);
        RegisterDynamicExceptionEvent("ExceptionCatchStart", () => sawTypedCatch, RecordCatch);

        source.Process();

        throwRoot.Total = totalThrown;
        throwRoot.Calls = totalThrown > int.MaxValue ? int.MaxValue : (int)totalThrown;

        CallTreeNode? catchRootResult = null;
        if (totalCaught > 0)
        {
            catchRoot.Total = totalCaught;
            catchRoot.Calls = totalCaught > int.MaxValue ? int.MaxValue : (int)totalCaught;
            catchRootResult = catchRoot;
        }

        foreach (var (typeName, count) in typeThrowCounts)
        {
            var typeRoot = CallTreeFrameWalker.GetOrCreateTypeRoot(typeThrowRoots, typeName);
            typeRoot.Total = count;
            typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
        }

        foreach (var (typeName, count) in typeCatchCounts)
        {
            var typeRoot = CallTreeFrameWalker.GetOrCreateTypeRoot(typeCatchRoots, typeName);
            typeRoot.Total = count;
            typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
        }

        foreach (var (typeName, thrownCount) in typeThrowCounts)
        {
            typeCatchCounts.TryGetValue(typeName, out var caughtCount);
            typeThrowRoots.TryGetValue(typeName, out var throwRootNode);
            typeCatchRoots.TryGetValue(typeName, out var catchRootNode);
            typeCatchSites.TryGetValue(typeName, out var sites);
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

        foreach (var (typeName, caughtCount) in typeCatchCounts)
        {
            if (typeDetails.ContainsKey(typeName))
            {
                continue;
            }

            typeCatchRoots.TryGetValue(typeName, out var catchRootNode);
            typeCatchSites.TryGetValue(typeName, out var sites);
            var siteList = ExceptionSiteSampleBuilder.Create(sites);

            typeDetails[typeName] = new ExceptionTypeDetails(
                0,
                new CallTreeNode(-1, "Total"),
                caughtCount,
                catchRootNode,
                siteList);
        }

        var exceptionTypes = exceptionCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ExceptionTypeSample(kv.Key, kv.Value))
            .ToList();

        var catchSiteList = ExceptionSiteSampleBuilder.Create(catchSites);

        return new ExceptionProfileResult(
            exceptionTypes,
            throwRoot,
            totalThrown,
            typeDetails,
            catchSiteList,
            catchRootResult,
            totalCaught);
    }
}
