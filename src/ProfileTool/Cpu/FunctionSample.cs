namespace Asynkron.Profiler;

internal sealed record FunctionSample(string Name, double TimeMs, int Calls, int FrameIdx);
