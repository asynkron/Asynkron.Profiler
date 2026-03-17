using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileInputDefaults
{
    public static void Apply(
        string inputPath,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        switch (Path.GetExtension(inputPath).ToLowerInvariant())
        {
            case ".json":
                runCpu = true;
                break;
            case ".nettrace":
                runCpu = true;
                runException = true;
                runContention = true;
                break;
            case ".etlx":
                runMemory = true;
                runException = true;
                runContention = true;
                break;
            case ".gcdump":
            case ".txt":
            case ".log":
                runHeap = true;
                break;
            default:
                runCpu = true;
                break;
        }
    }
}
