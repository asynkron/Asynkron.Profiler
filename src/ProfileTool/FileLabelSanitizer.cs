using System;
using System.IO;

namespace Asynkron.Profiler;

internal static class FileLabelSanitizer
{
    public static string Sanitize(string? value, string fallback)
    {
        var label = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            label = label.Replace(invalid, '_');
        }

        return label;
    }
}
