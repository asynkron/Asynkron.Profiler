using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Asynkron.Profiler;

public sealed class ProfilerAnalysisException : Exception
{
    public ProfilerAnalysisException(string message) : base(message)
    {
    }

    public ProfilerAnalysisException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class ProfilerTraceAnalyzer
{
    public ProfilerTraceAnalyzer(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        OutputDirectory = outputDirectory;
        Directory.CreateDirectory(OutputDirectory);
    }

    public string OutputDirectory { get; }

    public CpuProfileResult AnalyzeSpeedscope(string speedscopePath)
    {
        if (string.IsNullOrWhiteSpace(speedscopePath))
        {
            throw new ArgumentException("Speedscope path is required.", nameof(speedscopePath));
        }

        if (!File.Exists(speedscopePath))
        {
            throw new FileNotFoundException("Speedscope file not found.", speedscopePath);
        }

        var result = SpeedscopeParser.ParseFile(speedscopePath);
        if (result == null)
        {
            throw new ProfilerAnalysisException("Speedscope parse failed.");
        }

        return result;
    }

    public CpuProfileResult AnalyzeCpuTrace(string traceFile)
    {
        if (!File.Exists(traceFile))
        {
            throw new FileNotFoundException("Trace file not found.", traceFile);
        }

        try
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

            var etlxPath = ResolveEtlxPath(traceFile);
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

                var typeName = GetExceptionTypeName(data);
                var frames = EnumerateCpuFrames(stack).ToList();
                if (frames.Count == 0)
                {
                    frames.Add("Unknown");
                }

                frames.Reverse();

                var node = callTreeRoot;
                node.AddExceptionTotals(1);
                for (var i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    if (!frameIndices.TryGetValue(frame, out var frameIdx))
                    {
                        frameIdx = framesList.Count;
                        framesList.Add(frame);
                        frameIndices[frame] = frameIdx;
                    }

                    if (!node.Children.TryGetValue(frameIdx, out var child))
                    {
                        child = new CallTreeNode(frameIdx, frame);
                        node.Children[frameIdx] = child;
                    }

                    if (i == frames.Count - 1)
                    {
                        child.AddException(typeName, 1);
                    }
                    else
                    {
                        child.AddExceptionTotals(1);
                    }

                    node = child;
                }
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
                var frames = EnumerateCpuFrames(stack).ToList();
                if (frames.Count == 0)
                {
                    frames.Add("Unknown");
                }

                frames.Reverse();

                var node = callTreeRoot;
                node.AddAllocationTotals(bytes);
                for (var i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    if (!frameIndices.TryGetValue(frame, out var frameIdx))
                    {
                        frameIdx = framesList.Count;
                        framesList.Add(frame);
                        frameIndices[frame] = frameIdx;
                    }

                    if (!node.Children.TryGetValue(frameIdx, out var child))
                    {
                        child = new CallTreeNode(frameIdx, frame);
                        node.Children[frameIdx] = child;
                    }

                    if (i == frames.Count - 1)
                    {
                        child.AddAllocation(typeName, bytes);
                    }
                    else
                    {
                        child.AddAllocationTotals(bytes);
                    }

                    node = child;
                }
            };

            source.Clr.ExceptionStart += data =>
            {
                sawTypedException = true;
                RecordException(data);
            };

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionStart",
                data =>
                {
                    if (!sawTypedException)
                    {
                        RecordException(data);
                    }
                });

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionThrown",
                data =>
                {
                    if (!sawTypedException)
                    {
                        RecordException(data);
                    }
                });

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

                var frames = EnumerateCpuFrames(stack).ToList();
                if (frames.Count == 0)
                {
                    frames.Add("Unknown");
                }

                frames.Reverse();

                totalSamples++;
                callTreeTotal += weight;

                var node = callTreeRoot;
                for (var i = 0; i < frames.Count; i++)
                {
                    var frame = frames[i];
                    if (!frameIndices.TryGetValue(frame, out var frameIdx))
                    {
                        frameIdx = framesList.Count;
                        framesList.Add(frame);
                        frameIndices[frame] = frameIdx;
                    }

                    if (!node.Children.TryGetValue(frameIdx, out var child))
                    {
                        child = new CallTreeNode(frameIdx, frame);
                        node.Children[frameIdx] = child;
                    }

                    if (weight > 0)
                    {
                        child.Total += weight;
                    }

                    if (child.Calls < int.MaxValue)
                    {
                        child.Calls += 1;
                    }

                    frameCounts.TryGetValue(frame, out var count);
                    frameCounts[frame] = count + 1;

                    frameTotals.TryGetValue(frame, out var total);
                    frameTotals[frame] = total + weight;

                    node = child;
                }

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

            var allFunctions = frameTotals
                .OrderByDescending(kv => kv.Value)
                .Select(kv =>
                {
                    frameCounts.TryGetValue(kv.Key, out var calls);
                    frameIndices.TryGetValue(kv.Key, out var frameIdx);
                    return new FunctionSample(kv.Key, kv.Value, calls, frameIdx);
                })
                .ToList();

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
        catch (ProfilerAnalysisException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"CPU trace parse failed: {ex.Message}", ex);
        }
    }

    public AllocationCallTreeResult AnalyzeAllocationTrace(string traceFile)
    {
        if (!File.Exists(traceFile))
        {
            throw new FileNotFoundException("Trace file not found.", traceFile);
        }

        try
        {
            var typeRoots = new Dictionary<string, AllocationCallTreeNode>(StringComparer.Ordinal);
            long totalBytes = 0;
            long totalCount = 0;

            var etlxPath = ResolveEtlxPath(traceFile);
            using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
            using var source = traceLog.Events.GetSource();
            source.Clr.GCAllocationTick += data =>
            {
                var bytes = data.AllocationAmount64;
                if (bytes <= 0)
                {
                    return;
                }

                var typeName = string.IsNullOrWhiteSpace(data.TypeName) ? "Unknown" : data.TypeName;
                if (!typeRoots.TryGetValue(typeName, out var typeRoot))
                {
                    typeRoot = new AllocationCallTreeNode(typeName);
                    typeRoots[typeName] = typeRoot;
                }

                totalBytes += bytes;
                totalCount++;
                typeRoot.TotalBytes += bytes;
                typeRoot.Count++;

                var stack = data.CallStack();
                if (stack == null)
                {
                    return;
                }

                var node = typeRoot;
                foreach (var frame in EnumerateAllocationFrames(stack))
                {
                    if (string.IsNullOrWhiteSpace(frame))
                    {
                        continue;
                    }

                    if (!node.Children.TryGetValue(frame, out var child))
                    {
                        child = new AllocationCallTreeNode(frame);
                        node.Children[frame] = child;
                    }

                    child.TotalBytes += bytes;
                    child.Count++;
                    node = child;
                }
            };

            source.Process();

            var roots = typeRoots.Values
                .OrderByDescending(node => node.TotalBytes)
                .ToList();

            return new AllocationCallTreeResult(totalBytes, totalCount, roots);
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"Allocation trace parse failed: {ex.Message}", ex);
        }
    }

    public ExceptionProfileResult AnalyzeExceptionTrace(string traceFile)
    {
        if (!File.Exists(traceFile))
        {
            throw new FileNotFoundException("Trace file not found.", traceFile);
        }

        try
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

            var etlxPath = ResolveEtlxPath(traceFile);
            using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
            using var source = traceLog.Events.GetSource();

            var sawTypedThrow = false;
            var sawTypedCatch = false;

            void RecordThrow(TraceEvent data)
            {
                var typeName = GetExceptionTypeName(data);
                exceptionCounts[typeName] = exceptionCounts.TryGetValue(typeName, out var count)
                    ? count + 1
                    : 1;
                totalThrown += 1;
                RecordExceptionStack(
                    data.CallStack(),
                    throwRoot,
                    throwFrameIndices,
                    throwFramesList);

                typeThrowCounts[typeName] = typeThrowCounts.TryGetValue(typeName, out var typeCount)
                    ? typeCount + 1
                    : 1;
                if (!typeThrowRoots.TryGetValue(typeName, out var typeRoot))
                {
                    typeRoot = new CallTreeNode(-1, "Total");
                    typeThrowRoots[typeName] = typeRoot;
                }

                RecordExceptionStack(
                    data.CallStack(),
                    typeRoot,
                    throwFrameIndices,
                    throwFramesList);
            }

            void RecordCatch(TraceEvent data)
            {
                totalCaught += 1;
                RecordExceptionStack(
                    data.CallStack(),
                    catchRoot,
                    catchFrameIndices,
                    catchFramesList);

                var typeName = GetExceptionTypeName(data);
                typeCatchCounts[typeName] = typeCatchCounts.TryGetValue(typeName, out var typeCount)
                    ? typeCount + 1
                    : 1;
                if (!typeCatchRoots.TryGetValue(typeName, out var typeRoot))
                {
                    typeRoot = new CallTreeNode(-1, "Total");
                    typeCatchRoots[typeName] = typeRoot;
                }

                RecordExceptionStack(
                    data.CallStack(),
                    typeRoot,
                    catchFrameIndices,
                    catchFramesList);

                var catchSite = GetTopFrameName(data.CallStack()) ?? "Unknown";
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

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionStart",
                data =>
                {
                    if (!sawTypedThrow)
                    {
                        RecordThrow(data);
                    }
                });

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionThrown",
                data =>
                {
                    if (!sawTypedThrow)
                    {
                        RecordThrow(data);
                    }
                });

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ExceptionCatchStart",
                data =>
                {
                    if (!sawTypedCatch)
                    {
                        RecordCatch(data);
                    }
                });

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
                if (!typeThrowRoots.TryGetValue(typeName, out var typeRoot))
                {
                    typeRoot = new CallTreeNode(-1, "Total");
                    typeThrowRoots[typeName] = typeRoot;
                }

                typeRoot.Total = count;
                typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
            }

            foreach (var (typeName, count) in typeCatchCounts)
            {
                if (!typeCatchRoots.TryGetValue(typeName, out var typeRoot))
                {
                    typeRoot = new CallTreeNode(-1, "Total");
                    typeCatchRoots[typeName] = typeRoot;
                }

                typeRoot.Total = count;
                typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
            }

            foreach (var (typeName, thrownCount) in typeThrowCounts)
            {
                typeCatchCounts.TryGetValue(typeName, out var caughtCount);
                typeThrowRoots.TryGetValue(typeName, out var throwRootNode);
                typeCatchRoots.TryGetValue(typeName, out var catchRootNode);
                typeCatchSites.TryGetValue(typeName, out var sites);
                var siteList = (IReadOnlyList<ExceptionSiteSample>)(sites == null
                    ? Array.Empty<ExceptionSiteSample>()
                    : sites.OrderByDescending(kv => kv.Value)
                        .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
                        .ToList());

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
                var siteList = (IReadOnlyList<ExceptionSiteSample>)(sites == null
                    ? Array.Empty<ExceptionSiteSample>()
                    : sites.OrderByDescending(kv => kv.Value)
                        .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
                        .ToList());

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

            var catchSiteList = catchSites
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
                .ToList();

            return new ExceptionProfileResult(
                exceptionTypes,
                throwRoot,
                totalThrown,
                typeDetails,
                catchSiteList,
                catchRootResult,
                totalCaught);
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"Exception trace parse failed: {ex.Message}", ex);
        }
    }

    public ContentionProfileResult AnalyzeContentionTrace(string traceFile)
    {
        if (!File.Exists(traceFile))
        {
            throw new FileNotFoundException("Trace file not found.", traceFile);
        }

        try
        {
            var frameTotals = new Dictionary<string, double>(StringComparer.Ordinal);
            var frameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var frameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            var framesList = new List<string>();
            var callTreeRoot = new CallTreeNode(-1, "Total");
            var pending = new Dictionary<int, Stack<(double StartTime, TraceCallStack? Stack)>>();
            var totalWaitMs = 0d;
            long totalCount = 0;

            var etlxPath = ResolveEtlxPath(traceFile);
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

                var frames = EnumerateContentionFrames(stack).ToList();
                if (frames.Count == 0)
                {
                    frames.Add("Unknown");
                }

                frames.Reverse();

                var node = callTreeRoot;
                foreach (var frame in frames)
                {
                    if (!frameIndices.TryGetValue(frame, out var frameIdx))
                    {
                        frameIdx = framesList.Count;
                        framesList.Add(frame);
                        frameIndices[frame] = frameIdx;
                    }

                    if (!node.Children.TryGetValue(frameIdx, out var child))
                    {
                        child = new CallTreeNode(frameIdx, frame);
                        node.Children[frameIdx] = child;
                    }

                    child.Total += durationMs;
                    if (child.Calls < int.MaxValue)
                    {
                        child.Calls += 1;
                    }

                    frameTotals[frame] = frameTotals.TryGetValue(frame, out var total)
                        ? total + durationMs
                        : durationMs;
                    frameCounts[frame] = frameCounts.TryGetValue(frame, out var count)
                        ? count + 1
                        : 1;

                    node = child;
                }

                totalWaitMs += durationMs;
                totalCount += 1;
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
                data =>
                {
                    HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack());
                });

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ContentionStop_V2",
                data =>
                {
                    var durationMs = TryGetPayloadDurationMs(data);
                    HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
                });

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ContentionStart",
                data =>
                {
                    if (sawTypedEvent)
                    {
                        return;
                    }

                    HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack());
                });

            source.Dynamic.AddCallbackForProviderEvent(
                "Microsoft-Windows-DotNETRuntime",
                "ContentionStop",
                data =>
                {
                    if (sawTypedEvent)
                    {
                        return;
                    }

                    var durationMs = TryGetPayloadDurationMs(data);
                    HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
                });

            source.Process();

            callTreeRoot.Total = totalWaitMs;
            callTreeRoot.Calls = totalCount > int.MaxValue ? int.MaxValue : (int)totalCount;

            var topFunctions = frameTotals
                .OrderByDescending(kv => kv.Value)
                .Select(kv =>
                {
                    frameCounts.TryGetValue(kv.Key, out var calls);
                    frameIndices.TryGetValue(kv.Key, out var frameIdx);
                    return new FunctionSample(kv.Key, kv.Value, calls, frameIdx);
                })
                .ToList();

            return new ContentionProfileResult(topFunctions, callTreeRoot, totalWaitMs, totalCount);
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"Contention trace parse failed: {ex.Message}", ex);
        }
    }

    private string ResolveEtlxPath(string traceFile)
    {
        if (!traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            return traceFile;
        }

        var fileName = Path.GetFileNameWithoutExtension(traceFile);
        var targetPath = Path.Combine(OutputDirectory, $"{fileName}.etlx");
        var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
        return TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
    }

    private static void RecordExceptionStack(
        TraceCallStack? stack,
        CallTreeNode root,
        Dictionary<string, int> frameIndices,
        List<string> framesList)
    {
        var frames = EnumerateExceptionFrames(stack).ToList();
        if (frames.Count == 0)
        {
            frames.Add("Unknown");
        }

        frames.Reverse();
        var node = root;
        foreach (var frame in frames)
        {
            if (!frameIndices.TryGetValue(frame, out var frameIdx))
            {
                frameIdx = framesList.Count;
                framesList.Add(frame);
                frameIndices[frame] = frameIdx;
            }

            if (!node.Children.TryGetValue(frameIdx, out var child))
            {
                child = new CallTreeNode(frameIdx, frame);
                node.Children[frameIdx] = child;
            }

            child.Total += 1;
            if (child.Calls < int.MaxValue)
            {
                child.Calls += 1;
            }

            node = child;
        }
    }

    private static IEnumerable<string> EnumerateExceptionFrames(TraceCallStack? stack)
    {
        if (stack == null)
        {
            yield break;
        }

        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = current.CodeAddress?.FullMethodName;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                methodName = current.CodeAddress?.Method?.FullMethodName;
            }

            if (!string.IsNullOrWhiteSpace(methodName))
            {
                yield return methodName;
            }
        }
    }

    private static IEnumerable<string> EnumerateCpuFrames(TraceCallStack? stack)
    {
        if (stack == null)
        {
            yield break;
        }

        var lastWasUnknown = false;
        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = current.CodeAddress?.FullMethodName;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                methodName = current.CodeAddress?.Method?.FullMethodName;
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                if (!lastWasUnknown)
                {
                    yield return "Unmanaged Code";
                    lastWasUnknown = true;
                }

                continue;
            }

            lastWasUnknown = false;
            yield return methodName;
        }
    }

    private static IEnumerable<string> EnumerateAllocationFrames(TraceCallStack stack)
    {
        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = current.CodeAddress?.FullMethodName;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                methodName = current.CodeAddress?.Method?.FullMethodName;
            }

            yield return methodName ?? "Unknown";
        }
    }

    private static IEnumerable<string> EnumerateContentionFrames(TraceCallStack? stack)
    {
        if (stack == null)
        {
            yield break;
        }

        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = current.CodeAddress?.FullMethodName;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                methodName = current.CodeAddress?.Method?.FullMethodName;
            }

            if (!string.IsNullOrWhiteSpace(methodName))
            {
                yield return methodName;
            }
        }
    }

    private static string? GetTopFrameName(TraceCallStack? stack)
    {
        if (stack == null)
        {
            return null;
        }

        var methodName = stack.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = stack.CodeAddress?.Method?.FullMethodName;
        }

        return string.IsNullOrWhiteSpace(methodName) ? null : methodName;
    }

    private static string GetExceptionTypeName(TraceEvent data)
    {
        var typeName = TryGetPayloadString(data, "ExceptionTypeName", "ExceptionType", "TypeName");
        if (string.IsNullOrWhiteSpace(typeName))
        {
            try
            {
                foreach (var payloadName in data.PayloadNames ?? Array.Empty<string>())
                {
                    if (!payloadName.Contains("ExceptionType", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = data.PayloadByName(payloadName);
                    if (value != null)
                    {
                        typeName = value.ToString();
                        break;
                    }
                }
            }
            catch
            {
                typeName = null;
            }
        }

        return string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName;
    }

    private static double TryGetPayloadDurationMs(TraceEvent data)
    {
        var durationNs = TryGetPayloadLong(data, "DurationNs")
                         ?? TryGetPayloadLong(data, "DurationNS")
                         ?? TryGetPayloadLong(data, "Duration");
        if (durationNs is > 0)
        {
            return durationNs.Value / 1_000_000d;
        }

        return 0d;
    }

    private static string? TryGetPayloadString(TraceEvent data, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var value = data.PayloadByName(name);
                if (value != null)
                {
                    return value.ToString();
                }
            }
            catch
            {
                // Ignore missing payloads.
            }
        }

        return null;
    }

    private static long? TryGetPayloadLong(TraceEvent data, string name)
    {
        try
        {
            var value = data.PayloadByName(name);
            if (value == null)
            {
                return null;
            }

            return value switch
            {
                byte v => v,
                sbyte v => v,
                short v => v,
                ushort v => v,
                int v => v,
                uint v => v,
                long v => v,
                ulong v => v <= long.MaxValue ? (long)v : null,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }
}
