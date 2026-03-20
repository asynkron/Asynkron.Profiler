using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileInputConventions
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
        switch (GetNormalizedExtension(inputPath))
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

    public static string GetNormalizedExtension(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant();
    }

    public static bool IsTraceExtension(string extension)
    {
        return extension is ".nettrace" or ".etlx";
    }
}
