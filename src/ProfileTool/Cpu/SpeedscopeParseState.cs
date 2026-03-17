using System;
using System.Collections.Generic;
using System.Linq;

namespace Asynkron.Profiler;

internal sealed class SpeedscopeParseState
{
    public Dictionary<int, double> FrameTimes { get; } = [];
    public Dictionary<int, double> FrameSelfTimes { get; } = [];
    public Dictionary<int, int> FrameCounts { get; } = [];
    public CallTreeNode CallTreeRoot { get; } = new(-1, "Total");

    public double CallTreeTotal { get; set; }
    public bool HasSampledProfile { get; set; }
    public bool HasSampleUnit { get; set; }
    public bool HasTimeUnit { get; set; }
    public int ParsedProfiles { get; set; }

    public CpuProfileResult BuildResult(IReadOnlyList<string> framesList, string? speedscopePath)
    {
        var totalTime = FrameTimes.Values.Sum();

        if (CallTreeTotal <= 0)
        {
            CallTreeTotal = SumCallTreeTotals(CallTreeRoot);
        }

        CallTreeRoot.Total = CallTreeTotal;
        CallTreeRoot.Calls = SumCallTreeCalls(CallTreeRoot);

        foreach (var child in CallTreeRoot.Children.Values)
        {
            if (child.HasTiming)
            {
                CallTreeRoot.UpdateTiming(child.MinStart, child.MaxEnd);
            }
        }

        var allFunctions = FrameTimes
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                var name = kv.Key < framesList.Count ? framesList[kv.Key] : "Unknown";
                FrameCounts.TryGetValue(kv.Key, out var calls);
                return new FunctionSample(name, kv.Value, calls, kv.Key);
            })
            .ToList();

        var timeUnitLabel = HasSampleUnit && !HasTimeUnit ? "samples" : "ms";
        var countLabel = HasSampledProfile ? "Samples" : "Calls";
        var countSuffix = HasSampledProfile ? " samp" : "x";

        return new CpuProfileResult(
            allFunctions,
            totalTime,
            CallTreeRoot,
            CallTreeTotal,
            speedscopePath,
            timeUnitLabel,
            countLabel,
            countSuffix);
    }

    public static CallTreeNode GetOrCreateCallTreeChild(
        CallTreeNode parent,
        int frameIdx,
        IReadOnlyList<string> frames)
    {
        if (!parent.Children.TryGetValue(frameIdx, out var child))
        {
            var name = frameIdx >= 0 && frameIdx < frames.Count ? frames[frameIdx] : "Unknown";
            child = new CallTreeNode(frameIdx, name);
            parent.Children[frameIdx] = child;
        }

        return child;
    }

    private static double SumCallTreeTotals(CallTreeNode node)
    {
        var sum = 0d;
        foreach (var child in node.Children.Values)
        {
            sum += child.Total;
        }

        return sum;
    }

    private static int SumCallTreeCalls(CallTreeNode node)
    {
        var sum = 0;
        foreach (var child in node.Children.Values)
        {
            sum += child.Calls;
        }

        return sum;
    }
}
