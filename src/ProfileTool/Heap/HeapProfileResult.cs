using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed record HeapProfileResult(string? RawOutput, IReadOnlyList<HeapTypeEntry> Types);
