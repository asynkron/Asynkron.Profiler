using System;
using System.IO;

namespace Asynkron.Profiler;

public static class ArgumentGuard
{
    public static string RequireNotWhiteSpace(string? value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }

        return value;
    }

    public static string RequireExistingFile(string? path, string paramName, string requiredMessage, string missingMessage)
    {
        var validatedPath = RequireNotWhiteSpace(path, paramName, requiredMessage);
        if (!File.Exists(validatedPath))
        {
            throw new FileNotFoundException(missingMessage, validatedPath);
        }

        return validatedPath;
    }
}
