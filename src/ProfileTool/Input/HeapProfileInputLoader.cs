using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class HeapProfileInputLoader
{
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly Action<string> _writeLine;
    private readonly string _dotnetGcdumpInstallHint;

    public HeapProfileInputLoader(
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        _getTheme = getTheme;
        _ensureToolAvailable = ensureToolAvailable;
        _runProcess = runProcess;
        _parseGcdumpReport = parseGcdumpReport;
        _writeLine = writeLine;
        _dotnetGcdumpInstallHint = dotnetGcdumpInstallHint;
    }

    public HeapProfileResult? Load(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        return ProfileInputKindResolver.Resolve(inputPath) switch
        {
            ProfileInputKind.Gcdump => LoadGcdump(inputPath),
            ProfileInputKind.HeapReport => _parseGcdumpReport(File.ReadAllText(inputPath)),
            _ => WriteUnsupportedInput(inputPath)
        };
    }

    private HeapProfileResult? LoadGcdump(string inputPath)
    {
        if (!_ensureToolAvailable("dotnet-gcdump", _dotnetGcdumpInstallHint))
        {
            return null;
        }

        return GcdumpReportLoader.Load(
            inputPath,
            _getTheme(),
            _runProcess,
            _parseGcdumpReport,
            _writeLine);
    }

    private bool TryEnsureInputExists(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return true;
        }

        _writeLine($"[{_getTheme().ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return false;
    }

    private HeapProfileResult? WriteUnsupportedInput(string inputPath)
    {
        _writeLine($"[{_getTheme().ErrorColor}]Unsupported heap input:[/] {Markup.Escape(inputPath)}");
        return null;
    }
}
