using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileInputLabelBuilder
{
    public static string Build(string inputPath)
    {
        return FileLabelSanitizer.Sanitize(Path.GetFileNameWithoutExtension(inputPath), "input");
    }
}
