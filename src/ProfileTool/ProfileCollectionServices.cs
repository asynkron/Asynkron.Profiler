using System;
using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileCollectionServices
{
    public ProfileCollectionServices(
        string outputDirectory,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Action<string> writeLine)
    {
        OutputDirectory = ArgumentGuard.RequireNotWhiteSpace(outputDirectory, nameof(outputDirectory), "Output directory is required.");
        GetTheme = getTheme;
        EnsureToolAvailable = ensureToolAvailable;
        RunProcess = runProcess;
        WriteLine = writeLine;
    }

    public Func<string, string, bool> EnsureToolAvailable { get; }

    public Func<Theme> GetTheme { get; }

    public string OutputDirectory { get; }

    public Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> RunProcess { get; }

    public Action<string> WriteLine { get; }

    public void WriteError(string message)
    {
        WriteLine($"[{GetTheme().ErrorColor}]{message}[/]");
    }

    public void WriteFailure(string label, string detail)
    {
        WriteLine($"[{GetTheme().ErrorColor}]{label}:[/] {Markup.Escape(detail)}");
    }
}
