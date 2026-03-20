using System.IO;

namespace Asynkron.Profiler;

internal enum ProfileInputKind
{
    Unknown,
    SpeedscopeJson,
    NetTrace,
    Etlx,
    Gcdump,
    HeapTextReport,
    HeapLogReport
}

internal readonly record struct ProfileInputModes(
    bool RunCpu,
    bool RunMemory,
    bool RunHeap,
    bool RunException,
    bool RunContention);

internal static class ProfileInputCatalog
{
    public static string BuildLabel(string inputPath)
    {
        return FileLabelSanitizer.Sanitize(Path.GetFileNameWithoutExtension(inputPath), "input");
    }

    public static ProfileInputKind GetKind(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant() switch
        {
            ".json" => ProfileInputKind.SpeedscopeJson,
            ".nettrace" => ProfileInputKind.NetTrace,
            ".etlx" => ProfileInputKind.Etlx,
            ".gcdump" => ProfileInputKind.Gcdump,
            ".txt" => ProfileInputKind.HeapTextReport,
            ".log" => ProfileInputKind.HeapLogReport,
            _ => ProfileInputKind.Unknown
        };
    }

    public static ProfileInputModes GetDefaultModes(string inputPath)
    {
        return GetKind(inputPath) switch
        {
            ProfileInputKind.SpeedscopeJson => new(true, false, false, false, false),
            ProfileInputKind.NetTrace => new(true, false, false, true, true),
            ProfileInputKind.Etlx => new(false, true, false, true, true),
            ProfileInputKind.Gcdump or ProfileInputKind.HeapTextReport or ProfileInputKind.HeapLogReport
                => new(false, false, true, false, false),
            _ => new(true, false, false, false, false)
        };
    }

    public static bool IsTraceInput(ProfileInputKind kind)
    {
        return kind is ProfileInputKind.NetTrace or ProfileInputKind.Etlx;
    }
}
