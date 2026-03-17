using System;
using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed class CallTreeNode
{
    private const string UnknownRootName = "Total";

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
        if (!IsPositive(bytes)) return;

        AllocationBytes += bytes;
        AllocationCount = IncrementClamped(AllocationCount);
    }

    public void AddAllocation(string typeName, long bytes)
    {
        if (!IsPositive(bytes)) return;

        AddAllocationTotals(bytes);

        AllocationByType ??= new Dictionary<string, long>(StringComparer.Ordinal);
        AllocationByType[typeName] = AllocationByType.TryGetValue(typeName, out var existing)
            ? existing + bytes
            : bytes;

        AllocationCountByType ??= new Dictionary<string, int>(StringComparer.Ordinal);
        AllocationCountByType[typeName] = AllocationCountByType.TryGetValue(typeName, out var count)
            ? IncrementClamped(count)
            : 1;
    }

    public void AddExceptionTotals(long count)
    {
        if (!IsPositive(count)) return;

        ExceptionCount += count;
    }

    public void IncrementCalls()
    {
        Calls = IncrementClamped(Calls);
    }

    public void AddException(string typeName, long count)
    {
        if (!IsPositive(count)) return;

        AddExceptionTotals(count);

        ExceptionByType ??= new Dictionary<string, long>(StringComparer.Ordinal);
        ExceptionByType[typeName] = ExceptionByType.TryGetValue(typeName, out var existing)
            ? existing + count
            : count;
    }

    public static CallTreeNode CreateRoot()
    {
        return new CallTreeNode(-1, UnknownRootName);
    }

    private static bool IsPositive(long value)
    {
        return value > 0;
    }

    private static int IncrementClamped(int value)
    {
        return value < int.MaxValue ? value + 1 : value;
    }
}
