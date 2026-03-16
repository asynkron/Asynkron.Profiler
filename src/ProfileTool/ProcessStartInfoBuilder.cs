using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Asynkron.Profiler;

internal static class ProcessStartInfoBuilder
{
    public static ProcessStartInfo Create(string[] command, string? workingDirectory = null)
    {
        if (command.Length == 0)
        {
            throw new ArgumentException("Command is required.", nameof(command));
        }

        var psi = Create(command[0], command.AsSpan(1).ToArray(), workingDirectory);
        return psi;
    }

    public static ProcessStartInfo Create(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        return psi;
    }
}
