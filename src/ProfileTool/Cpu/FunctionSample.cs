namespace Asynkron.Profiler;

public sealed record FunctionSample(string Name, double TimeMs, long Calls, int FrameIdx);
