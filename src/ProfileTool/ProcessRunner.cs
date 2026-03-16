using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProcessRunner
{
    private static readonly string[] VersionProbeArgs = ["--version"];
    private readonly Dictionary<string, bool> _toolAvailability = new(StringComparer.OrdinalIgnoreCase);

    public (bool Success, string StdOut, string StdErr) RunProcess(
        string fileName,
        IEnumerable<string> args,
        string? workingDir = null,
        int timeoutMs = 300000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                psi.WorkingDirectory = workingDir;
            }

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return (false, string.Empty, "Failed to start process.");
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutLock = new object();
            var stderrLock = new object();

            AttachProcessDataHandlers(
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

    public bool EnsureToolAvailable(string toolName, string installHint, Theme theme)
    {
        if (_toolAvailability.TryGetValue(toolName, out var cached))
        {
            return cached;
        }

        var (success, _, stderr) = RunProcess(toolName, VersionProbeArgs, timeoutMs: 10000);
        if (!success)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "Tool not found." : stderr.Trim();
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]{toolName} unavailable:[/] {Markup.Escape(detail)}");
            AnsiConsole.MarkupLine($"[{theme.AccentColor}]Install:[/] {Markup.Escape(installHint)}");
            _toolAvailability[toolName] = false;
            return false;
        }

        _toolAvailability[toolName] = true;
        return true;
    }

    internal static void AttachProcessDataHandlers(Process process, Action<string> onStdout, Action<string> onStderr)
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
}
