using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Asynkron.Profiler;

internal static class CommandStartInfoFactory
{
    public static ProcessStartInfo Create(string[] command, string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Length == 0)
        {
            throw new ArgumentException("Command is required.", nameof(command));
        }

        return Create(command[0], command, workingDirectory, 1);
    }

    public static ProcessStartInfo Create(
        string fileName,
        IReadOnlyList<string> commandArguments,
        string? workingDirectory = null,
        int startIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(commandArguments);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            processStartInfo.WorkingDirectory = workingDirectory;
        }

        AddArguments(processStartInfo.ArgumentList, commandArguments, startIndex);
        return processStartInfo;
    }

    public static void AddArguments(ICollection<string> arguments, IReadOnlyList<string> command, int startIndex = 1)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(command);

        for (var index = startIndex; index < command.Count; index++)
        {
            arguments.Add(command[index]);
        }
    }
}
