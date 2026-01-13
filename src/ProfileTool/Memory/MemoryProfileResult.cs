using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed record MemoryProfileResult(
    string? Iterations,
    string? TotalTime,
    string? PerIterationTime,
    string? TotalAllocated,
    string? PerIterationAllocated,
    string? Gen0Collections,
    string? Gen1Collections,
    string? Gen2Collections,
    string? ParseAllocated,
    string? EvaluateAllocated,
    string? HeapBefore,
    string? HeapAfter,
    string? AllocationTotal,
    IReadOnlyList<AllocationEntry> AllocationEntries,
    AllocationCallTreeResult? AllocationCallTree,
    string? AllocationByTypeRaw,
    string? RawOutput);
