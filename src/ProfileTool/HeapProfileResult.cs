using System.Collections.Generic;

namespace Asynkron.JsEngine.Tools.ProfileTool;

internal sealed record HeapProfileResult(string? RawOutput, IReadOnlyList<HeapTypeEntry> Types);
