using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileInputPath
{
    public static string BuildLabel(string inputPath)
    {
        return FileLabelSanitizer.Sanitize(Path.GetFileNameWithoutExtension(inputPath), "input");
    }

    public static void ApplyDefaults(
        string inputPath,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        switch (GetKind(inputPath))
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

    public static bool IsTraceInput(string inputPath)
    {
        return IsTraceInput(GetKind(inputPath));
    }

    public static bool IsTraceInput(ProfileInputKind inputKind)
    {
        return inputKind is ProfileInputKind.NetTrace or ProfileInputKind.Etlx;
    }

    public static ProfileInputKind GetKind(string inputPath)
    {
        return GetNormalizedExtension(inputPath) switch
        {
            ".json" => ProfileInputKind.Speedscope,
            ".nettrace" => ProfileInputKind.NetTrace,
            ".etlx" => ProfileInputKind.Etlx,
            ".gcdump" => ProfileInputKind.Gcdump,
            ".txt" or ".log" => ProfileInputKind.HeapReport,
            _ => ProfileInputKind.Unknown
        };
    }

    private static string GetNormalizedExtension(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant();
    }
}
