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
    public long ExceptionCount { get; set; }
    public Dictionary<string, long>? AllocationByType { get; private set; }
    public Dictionary<string, int>? AllocationCountByType { get; private set; }
    public Dictionary<string, long>? ExceptionByType { get; private set; }
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

    public void AddAllocationTotals(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        AllocationBytes += bytes;
        AllocationCount = IncrementIfPossible(AllocationCount);
    }

    public void AddAllocation(string typeName, long bytes)
    {
        AddAllocationTotals(bytes);
        if (bytes <= 0)
        {
            return;
        }

        AllocationByType ??= new Dictionary<string, long>(StringComparer.Ordinal);
        AddToTotals(AllocationByType, typeName, bytes);

        AllocationCountByType ??= new Dictionary<string, int>(StringComparer.Ordinal);
        IncrementCount(AllocationCountByType, typeName);
    }

    public void AddExceptionTotals(long count)
    {
        if (count <= 0)
        {
            return;
        }

        ExceptionCount += count;
    }

    public void IncrementCalls()
    {
        Calls = IncrementIfPossible(Calls);
    }

    public void AddException(string typeName, long count)
    {
        if (count <= 0)
        {
            return;
        }

        AddExceptionTotals(count);

        ExceptionByType ??= new Dictionary<string, long>(StringComparer.Ordinal);
        AddToTotals(ExceptionByType, typeName, count);
    }

    private static void AddToTotals(Dictionary<string, long> totals, string key, long amount)
    {
        totals[key] = totals.TryGetValue(key, out var existing)
            ? existing + amount
            : amount;
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var count);
        counts[key] = count < int.MaxValue ? count + 1 : count;
    }

    private static int IncrementIfPossible(int value)
    {
        if (value < int.MaxValue)
        {
            return value + 1;
        }

        return value;
    }
}
