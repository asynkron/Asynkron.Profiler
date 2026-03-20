using System;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileLoadReporter
{
    private readonly Func<Theme> _getTheme;
    private readonly Action<string> _writeLine;

    public ProfileLoadReporter(Func<Theme> getTheme, Action<string> writeLine)
    {
        _getTheme = getTheme;
        _writeLine = writeLine;
    }

    public void WriteInputNotFound(string inputPath)
    {
        WriteError("Input file not found", inputPath);
    }

    public void WriteUnsupportedInput(string message, string inputPath)
    {
        WriteError(message, inputPath);
    }

    public void WriteNoCpuSamplesFound()
    {
        _writeLine($"[{_getTheme().AccentColor}]No CPU samples found in trace.[/]");
    }

    public void WriteParseFailure(string message, Exception ex)
    {
        _writeLine($"[{_getTheme().AccentColor}]{message}:[/] {Markup.Escape(ex.Message)}");
    }

    private void WriteError(string label, string value)
    {
        _writeLine($"[{_getTheme().ErrorColor}]{label}:[/] {Markup.Escape(value)}");
    }
}
