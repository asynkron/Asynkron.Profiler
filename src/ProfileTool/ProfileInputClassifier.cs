using System;
using System.IO;

namespace Asynkron.Profiler;

internal enum ProfileInputKind
{
    Unknown,
    SpeedscopeJson,
    NetTrace,
    EtlxTrace,
    Gcdump,
    HeapTextReport
}

internal static class ProfileInputClassifier
{
    public static ProfileInputKind Classify(string inputPath)
    {
        return GetNormalizedExtension(inputPath) switch
        {
            ".json" => ProfileInputKind.SpeedscopeJson,
            ".nettrace" => ProfileInputKind.NetTrace,
            ".etlx" => ProfileInputKind.EtlxTrace,
            ".gcdump" => ProfileInputKind.Gcdump,
            ".txt" or ".log" => ProfileInputKind.HeapTextReport,
            _ => ProfileInputKind.Unknown
        };
    }

    public static bool SupportsCpu(ProfileInputKind inputKind)
    {
        return inputKind is ProfileInputKind.SpeedscopeJson or ProfileInputKind.NetTrace or ProfileInputKind.EtlxTrace;
    }

    public static bool SupportsTraceAnalysis(ProfileInputKind inputKind)
    {
        return inputKind is ProfileInputKind.NetTrace or ProfileInputKind.EtlxTrace;
    }

    public static bool SupportsHeap(ProfileInputKind inputKind)
    {
        return inputKind is ProfileInputKind.Gcdump or ProfileInputKind.HeapTextReport;
    }

    public static void ApplyDefaults(
        ProfileInputKind inputKind,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        switch (inputKind)
        {
            case ProfileInputKind.SpeedscopeJson:
                runCpu = true;
                break;
            case ProfileInputKind.NetTrace:
                runCpu = true;
                runException = true;
                runContention = true;
                break;
            case ProfileInputKind.EtlxTrace:
                runMemory = true;
                runException = true;
                runContention = true;
                break;
            case ProfileInputKind.Gcdump:
            case ProfileInputKind.HeapTextReport:
                runHeap = true;
                break;
            default:
                runCpu = true;
                break;
        }
    }

    private static string GetNormalizedExtension(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant();
    }
}
