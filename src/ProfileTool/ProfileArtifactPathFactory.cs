using System;
using System.Globalization;
using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileArtifactPathFactory
{
    public static string Create(string outputDirectory, string label, string suffix)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(outputDirectory, $"{label}_{timestamp}.{suffix}");
    }
}
