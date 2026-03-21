using System.Collections.Generic;
using System.Linq;

namespace Asynkron.Profiler;

internal sealed class SpeedscopeAggregationState
{
    private const string UnknownFrameName = "Unknown";

    private readonly IReadOnlyList<string> _frames;
    private readonly Dictionary<int, double> _frameTimes = new();
    private readonly Dictionary<int, int> _frameCounts = new();

    public SpeedscopeAggregationState(IReadOnlyList<string> frames)
    {
        _frames = frames;
    }

    public CallTreeNode Root { get; } = CallTreeNode.CreateRoot();

    public double CallTreeTotal { get; private set; }

    private bool HasSampledProfile { get; set; }

    private bool HasSampleUnit { get; set; }

    private bool HasTimeUnit { get; set; }

    private int ParsedProfiles { get; set; }

    public void RegisterEventedProfile()
    {
        ParsedProfiles++;
        HasTimeUnit = true;
    }

    public void RegisterSampledProfile(bool isSampleUnit)
    {
        ParsedProfiles++;
        HasSampledProfile = true;
        if (isSampleUnit)
        {
            HasSampleUnit = true;
        }
        else
        {
            HasTimeUnit = true;
        }
    }

    public CallTreeNode GetOrCreateChild(CallTreeNode parent, int frameIdx)
    {
        if (!parent.Children.TryGetValue(frameIdx, out var child))
        {
            child = new CallTreeNode(frameIdx, GetFrameName(frameIdx));
            parent.Children[frameIdx] = child;
        }

        return child;
    }

    public void AddFrameTime(int frameIdx, double duration)
    {
        _frameTimes.TryGetValue(frameIdx, out var time);
        _frameTimes[frameIdx] = time + duration;
    }

    public void AddFrameCalls(int frameIdx, int calls)
    {
        _frameCounts.TryGetValue(frameIdx, out var currentCalls);
        _frameCounts[frameIdx] = currentCalls + calls;
    }

    public void AddRootDuration(double duration)
    {
        CallTreeTotal += duration;
    }

    public CpuProfileResult? BuildResult(string? speedscopePath)
    {
        if (ParsedProfiles == 0)
        {
            return null;
        }

        var totalTime = _frameTimes.Values.Sum();
        FinalizeRoot();

        var allFunctions = _frameTimes
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                _frameCounts.TryGetValue(kv.Key, out var calls);
                return new FunctionSample(GetFrameName(kv.Key), kv.Value, calls, kv.Key);
            })
            .ToList();

        var timeUnitLabel = HasSampleUnit && !HasTimeUnit ? "samples" : "ms";
        var countLabel = HasSampledProfile ? "Samples" : "Calls";
        var countSuffix = HasSampledProfile ? " samp" : "x";

        return new CpuProfileResult(
            allFunctions,
            totalTime,
            Root,
            CallTreeTotal,
            speedscopePath,
            timeUnitLabel,
            countLabel,
            countSuffix);
    }

    private string GetFrameName(int frameIdx)
    {
        return frameIdx >= 0 && frameIdx < _frames.Count
            ? _frames[frameIdx]
            : UnknownFrameName;
    }

    private void FinalizeRoot()
    {
        if (CallTreeTotal <= 0)
        {
            CallTreeTotal = SumCallTreeTotals(Root);
        }

        Root.Total = CallTreeTotal;
        Root.Calls = SumCallTreeCalls(Root);

        foreach (var child in Root.Children.Values)
        {
            if (child.HasTiming)
            {
                Root.UpdateTiming(child.MinStart, child.MaxEnd);
            }
        }
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
