namespace Asynkron.Profiler;

public sealed record AllocationEntry(string Type, long Count, string? Total);
