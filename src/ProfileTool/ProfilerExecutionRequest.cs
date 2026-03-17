namespace Asynkron.Profiler;

internal sealed record ProfilerExecutionRequest(
    string Label,
    string Description,
    string[] Command,
    string? InputPath,
    bool HasInput,
    bool HasExplicitModes,
    bool RunCpu,
    bool RunMemory,
    bool RunHeap,
    bool RunException,
    bool RunContention,
    bool JitInline,
    bool JitDisasm,
    bool JitDisasmHot,
    bool Jit,
    bool HotThresholdSpecified,
    string? JitMethod,
    string? JitAltJitPath,
    string? JitAltJitName,
    ProfileRenderRequest RenderRequest)
{
    public bool HasHotJitRequest => JitDisasmHot || HotThresholdSpecified;

    public bool ShouldRunHotJitDisasm => HasHotJitRequest && Jit;
}
