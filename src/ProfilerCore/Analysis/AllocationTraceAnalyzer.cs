using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class AllocationTraceAnalyzer
{
    public static AllocationCallTreeResult Analyze(string etlxPath)
    {
        try
        {
            var typeRoots = new Dictionary<string, AllocationCallTreeNode>(StringComparer.Ordinal);
            long totalBytes = 0;
            long totalCount = 0;

            using var session = TraceLogSession.Open(etlxPath);
            session.Source.Clr.GCAllocationTick += data =>
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
                foreach (var frame in TraceCallStackFrameEnumerator.EnumerateAllocationFrames(stack))
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

            session.Source.Process();

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
}
