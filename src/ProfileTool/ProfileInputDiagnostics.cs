using System;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileInputDiagnostics
{
    private readonly Func<Theme> _getTheme;
    private readonly Action<string> _writeLine;

    public ProfileInputDiagnostics(Func<Theme> getTheme, Action<string> writeLine)
    {
        _getTheme = getTheme;
        _writeLine = writeLine;
    }

    public bool TryEnsureInputExists(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return true;
        }

        _writeLine($"[{_getTheme().ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return false;
    }

    public void WriteUnsupportedInput(string message, string inputPath)
    {
        _writeLine($"[{_getTheme().ErrorColor}]{message}:[/] {Markup.Escape(inputPath)}");
    }
}
