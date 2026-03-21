using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileInputKindResolver
{
    public static ProfileInputKind Resolve(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant() switch
        {
            ".json" => ProfileInputKind.Speedscope,
            ".nettrace" => ProfileInputKind.NetTrace,
            ".etlx" => ProfileInputKind.Etlx,
            ".gcdump" => ProfileInputKind.Gcdump,
            ".txt" or ".log" => ProfileInputKind.HeapReport,
            _ => ProfileInputKind.Unknown
        };
    }

    public static bool IsTrace(ProfileInputKind kind)
    {
        return kind is ProfileInputKind.NetTrace or ProfileInputKind.Etlx;
    }
}
