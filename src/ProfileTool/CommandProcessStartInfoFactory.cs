using System.Diagnostics;

namespace Asynkron.Profiler;

internal static class CommandProcessStartInfoFactory
{
    public static ProcessStartInfo Create(string[] command, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        for (var i = 1; i < command.Length; i++)
        {
            psi.ArgumentList.Add(command[i]);
        }

        return psi;
    }
}
