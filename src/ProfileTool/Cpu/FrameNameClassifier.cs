using System;

namespace Asynkron.Profiler;

internal static class FrameNameClassifier
{
    public static bool ContainsLetter(string? value)
    {
        foreach (var ch in value ?? string.Empty)
        {
            if (char.IsLetter(ch))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsExplicitUnmanagedCode(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed.Contains("UNMANAGED_CODE_TIME", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "Unmanaged Code", StringComparison.OrdinalIgnoreCase);
    }

    public static bool StartsWithDigit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.TrimStart();
        return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
    }
}
