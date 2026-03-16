using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Asynkron.Profiler;

internal static class ProcessRunner
{
    public static void AttachDataHandlers(Process process, Action<string> onStdout, Action<string> onStderr)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onStdout(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onStderr(e.Data);
            }
        };
    }

    public static (bool Success, string StdOut, string StdErr) Run(
        string fileName,
        IEnumerable<string> args,
        string? workingDir = null,
        int timeoutMs = 300000)
    {
        try
        {
            var psi = ProcessStartInfoBuilder.Create(fileName, args, workingDir);
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return (false, string.Empty, "Failed to start process.");
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutLock = new object();
            var stderrLock = new object();

            AttachDataHandlers(
                process,
                data =>
                {
                    lock (stdoutLock)
                    {
                        stdout.AppendLine(data);
                    }
                },
                data =>
                {
                    lock (stderrLock)
                    {
                        stderr.AppendLine(data);
                    }
                });

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures.
                }

                return (false, stdout.ToString(), $"Process timed out after {timeoutMs} ms.");
            }

            process.WaitForExit();
            return (process.ExitCode == 0, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }
}
