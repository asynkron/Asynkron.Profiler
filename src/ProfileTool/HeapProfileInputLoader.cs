using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class HeapProfileInputLoader
{
    private readonly ProfileInputDiagnostics _diagnostics;
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly Action<string> _writeLine;
    private readonly string _dotnetGcdumpInstallHint;

    public HeapProfileInputLoader(
        Func<Theme> getTheme,
        ProfileInputDiagnostics diagnostics,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        _diagnostics = diagnostics;
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

        var extension = ProfileInputConventions.GetNormalizedExtension(inputPath);
        if (extension == ".gcdump")
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

        if (extension is ".txt" or ".log")
        {
            return _parseGcdumpReport(File.ReadAllText(inputPath));
        }

        _diagnostics.WriteUnsupportedInput("Unsupported heap input", inputPath);
        return null;
    }

    private bool TryEnsureInputExists(string inputPath)
    {
        return _diagnostics.TryEnsureInputExists(inputPath);
    }
}
