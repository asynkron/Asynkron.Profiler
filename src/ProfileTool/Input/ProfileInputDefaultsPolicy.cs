namespace Asynkron.Profiler;

internal static class ProfileInputDefaultsPolicy
{
    public static void Apply(
        string inputPath,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        switch (ProfileInputKindResolver.Resolve(inputPath))
        {
            case ProfileInputKind.Speedscope:
                runCpu = true;
                break;
            case ProfileInputKind.NetTrace:
                runCpu = true;
                runException = true;
                runContention = true;
                break;
            case ProfileInputKind.Etlx:
                runMemory = true;
                runException = true;
                runContention = true;
                break;
            case ProfileInputKind.Gcdump:
            case ProfileInputKind.HeapReport:
                runHeap = true;
                break;
            default:
                runCpu = true;
                break;
        }
    }
}
