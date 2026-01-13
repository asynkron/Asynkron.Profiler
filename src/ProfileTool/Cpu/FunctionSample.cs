namespace Asynkron.Profiler;

public sealed record FunctionSample(string Name, double TimeMs, int Calls, int FrameIdx);
