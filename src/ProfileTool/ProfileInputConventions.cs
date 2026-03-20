using System.IO;

namespace Asynkron.Profiler;

internal enum ProfileInputKind
{
    Unknown,
    Speedscope,
    NetTrace,
    TraceLog,
    GcDump,
    HeapReport
}

internal static class ProfileInputConventions
{
    public static string BuildInputLabel(string inputPath)
    {
        return FileLabelSanitizer.Sanitize(Path.GetFileNameWithoutExtension(inputPath), "input");
    }

    public static void ApplyInputDefaults(
        string inputPath,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        switch (Classify(inputPath))
        {
            case ProfileInputKind.Speedscope:
                runCpu = true;
                break;
            case ProfileInputKind.NetTrace:
                runCpu = true;
                runException = true;
                runContention = true;
                break;
            case ProfileInputKind.TraceLog:
                runMemory = true;
                runException = true;
                runContention = true;
                break;
            case ProfileInputKind.GcDump:
            case ProfileInputKind.HeapReport:
                runHeap = true;
                break;
            default:
                runCpu = true;
                break;
        }
    }

    public static ProfileInputKind Classify(string inputPath)
    {
        return GetNormalizedExtension(inputPath) switch
        {
            ".json" => ProfileInputKind.Speedscope,
            ".nettrace" => ProfileInputKind.NetTrace,
            ".etlx" => ProfileInputKind.TraceLog,
            ".gcdump" => ProfileInputKind.GcDump,
            ".txt" or ".log" => ProfileInputKind.HeapReport,
            _ => ProfileInputKind.Unknown
        };
    }

    private static string GetNormalizedExtension(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant();
    }
}
