using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileInputPathValidator
{
    public static bool TryEnsureExists(string inputPath, ProfileLoadReporter reporter)
    {
        if (File.Exists(inputPath))
        {
            return true;
        }

        reporter.WriteInputNotFound(inputPath);
        return false;
    }
}
