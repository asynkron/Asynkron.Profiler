namespace Asynkron.Profiler;

internal sealed record AllocationEntry(string Type, long Count, string? Total);
