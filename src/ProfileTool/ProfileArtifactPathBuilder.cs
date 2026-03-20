using System;
using System.Globalization;
using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileArtifactPathBuilder
{
    public static string Build(string outputDirectory, string label, string suffix)
    {
        return Build(outputDirectory, label, suffix, DateTime.Now);
    }

    internal static string Build(string outputDirectory, string label, string suffix, DateTime timestamp)
    {
        var resolvedOutputDirectory = ArgumentGuard.RequireNotWhiteSpace(outputDirectory, nameof(outputDirectory), "Output directory is required.");
        var resolvedLabel = ArgumentGuard.RequireNotWhiteSpace(label, nameof(label), "Label is required.");
        var resolvedSuffix = ArgumentGuard.RequireNotWhiteSpace(suffix, nameof(suffix), "Suffix is required.");
        var timestampToken = timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(resolvedOutputDirectory, $"{resolvedLabel}_{timestampToken}.{resolvedSuffix}");
    }
}
