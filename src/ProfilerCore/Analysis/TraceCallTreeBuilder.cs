using System;
using System.Collections.Generic;
using System.Linq;
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
        VisitCallTreeFrames(
            TraceCallStackFrameEnumerator.EnumerateExceptionFrames(stack),
            root,
            frameIndices,
            framesList,
            static (child, _, _) =>
            {
                child.Total += 1;
                child.IncrementCalls();
            });
    }

    public static CallTreeNode VisitCallTreeFrames(
        IEnumerable<string> frames,
        CallTreeNode root,
        Dictionary<string, int> frameIndices,
        List<string> framesList,
        Action<CallTreeNode, string, bool> visitNode)
    {
        var orderedFrames = PrepareCallTreeFrames(frames);
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

    public static CallTreeNode GetOrCreateTypeRoot(
        Dictionary<string, CallTreeNode> typeRoots,
        string typeName)
    {
        if (!typeRoots.TryGetValue(typeName, out var typeRoot))
        {
            typeRoot = new CallTreeNode(-1, "Total");
            typeRoots[typeName] = typeRoot;
        }

        return typeRoot;
    }

    private static List<string> PrepareCallTreeFrames(IEnumerable<string> frames)
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
        if (!frameIndices.TryGetValue(frame, out var frameIdx))
        {
            frameIdx = framesList.Count;
            framesList.Add(frame);
            frameIndices[frame] = frameIdx;
        }

        if (!parent.Children.TryGetValue(frameIdx, out var child))
        {
            child = new CallTreeNode(frameIdx, frame);
            parent.Children[frameIdx] = child;
        }

        return child;
    }
}
