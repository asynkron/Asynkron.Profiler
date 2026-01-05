using System;
using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed class CallTreeNode
{
    public CallTreeNode(int frameIdx, string name)
    {
        FrameIdx = frameIdx;
        Name = name;
        MinStart = double.MaxValue;
        MaxEnd = double.MinValue;
    }

    public int FrameIdx { get; }
    public string Name { get; }
    public double Total { get; set; }
    public double Self { get; set; }
    public int Calls { get; set; }
    public long AllocationBytes { get; set; }
    public int AllocationCount { get; set; }
    public Dictionary<string, long>? AllocationByType { get; private set; }
    public Dictionary<int, CallTreeNode> Children { get; } = new();

    /// <summary>
    /// Earliest start time of any invocation of this node (for timeline rendering).
    /// </summary>
    public double MinStart { get; set; }

    /// <summary>
    /// Latest end time of any invocation of this node (for timeline rendering).
    /// </summary>
    public double MaxEnd { get; set; }

    /// <summary>
    /// Updates timing bounds with a new span.
    /// </summary>
    public void UpdateTiming(double startMs, double endMs)
    {
        if (startMs < MinStart) MinStart = startMs;
        if (endMs > MaxEnd) MaxEnd = endMs;
    }

    /// <summary>
    /// Whether this node has valid timing data.
    /// </summary>
    public bool HasTiming => MinStart < double.MaxValue && MaxEnd > double.MinValue;

    public void AddAllocation(string typeName, long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        AllocationBytes += bytes;
        if (AllocationCount < int.MaxValue)
        {
            AllocationCount += 1;
        }

        AllocationByType ??= new Dictionary<string, long>(StringComparer.Ordinal);
        AllocationByType[typeName] = AllocationByType.TryGetValue(typeName, out var existing)
            ? existing + bytes
            : bytes;
    }
}
