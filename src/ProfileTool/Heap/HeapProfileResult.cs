using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed record HeapProfileResult(string? RawOutput, IReadOnlyList<HeapTypeEntry> Types);
