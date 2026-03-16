using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class TraceCallTreeBuilder
{
    public static void RecordExceptionStack(
        TraceCallStack? stack,
        CallTreeNode root,
        Dictionary<string, int> frameIndices,
        List<string> framesList)
    {
        VisitFrames(
            EnumerateExceptionFrames(stack),
            root,
            frameIndices,
            framesList,
            static (child, _, _) =>
            {
                child.Total += 1;
                if (child.Calls < int.MaxValue)
                {
                    child.Calls += 1;
                }
            });
    }

    public static CallTreeNode VisitFrames(
        IEnumerable<string> frames,
        CallTreeNode root,
        Dictionary<string, int> frameIndices,
        List<string> framesList,
        Action<CallTreeNode, string, bool> visitNode)
    {
        var orderedFrames = PrepareFrames(frames);
        var node = root;
        for (var i = 0; i < orderedFrames.Count; i++)
        {
            var frame = orderedFrames[i];
            var child = GetOrCreateChildNode(node, frame, frameIndices, framesList);
            visitNode(child, frame, i == orderedFrames.Count - 1);
            node = child;
        }

        return node;
    }

    public static IEnumerable<string> EnumerateExceptionFrames(TraceCallStack? stack) => EnumerateResolvedFrameNames(stack);

    public static IEnumerable<string> EnumerateCpuFrames(TraceCallStack? stack)
    {
        var lastWasUnknown = false;
        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = GetFrameMethodName(current);
            if (methodName == null)
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

    public static IEnumerable<string> EnumerateAllocationFrames(TraceCallStack stack)
    {
        for (var current = stack; current != null; current = current.Caller)
        {
            yield return GetFrameMethodName(current) ?? "Unknown";
        }
    }

    public static IEnumerable<string> EnumerateContentionFrames(TraceCallStack? stack) => EnumerateResolvedFrameNames(stack);

    public static string? GetTopFrameName(TraceCallStack? stack) => GetFrameMethodName(stack);

    private static List<string> PrepareFrames(IEnumerable<string> frames)
    {
        var orderedFrames = frames.ToList();
        if (orderedFrames.Count == 0)
        {
            orderedFrames.Add("Unknown");
        }

        orderedFrames.Reverse();
        return orderedFrames;
    }

    private static CallTreeNode GetOrCreateChildNode(
        CallTreeNode parent,
        string frame,
        Dictionary<string, int> frameIndices,
        List<string> framesList)
    {
        if (!frameIndices.TryGetValue(frame, out var frameIndex))
        {
            frameIndex = framesList.Count;
            framesList.Add(frame);
            frameIndices[frame] = frameIndex;
        }

        if (!parent.Children.TryGetValue(frameIndex, out var child))
        {
            child = new CallTreeNode(frameIndex, frame);
            parent.Children[frameIndex] = child;
        }

        return child;
    }

    private static string? GetFrameMethodName(TraceCallStack? stack)
    {
        var methodName = stack?.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = stack?.CodeAddress?.Method?.FullMethodName;
        }

        return string.IsNullOrWhiteSpace(methodName) ? null : methodName;
    }

    private static IEnumerable<string> EnumerateResolvedFrameNames(TraceCallStack? stack)
    {
        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = GetFrameMethodName(current);
            if (methodName != null)
            {
                yield return methodName;
            }
        }
    }
}
