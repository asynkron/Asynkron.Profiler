using System;
using System.Collections.Generic;
using System.IO;

namespace Asynkron.Profiler;

internal sealed class HeapProfileInputService
{
    private readonly string _dotnetGcdumpInstallHint;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly ProfileLoadReporter _reporter;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Action<string> _writeLine;

    public HeapProfileInputService(
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint,
        ProfileLoadReporter reporter)
    {
        _getTheme = getTheme;
        _ensureToolAvailable = ensureToolAvailable;
        _runProcess = runProcess;
        _parseGcdumpReport = parseGcdumpReport;
        _writeLine = writeLine;
        _dotnetGcdumpInstallHint = dotnetGcdumpInstallHint;
        _reporter = reporter;
    }

    public HeapProfileResult? LoadHeap(string inputPath)
    {
        if (!ProfileInputPathValidator.TryEnsureExists(inputPath, _reporter))
        {
            return null;
        }

        return ProfileInputCatalog.GetKind(inputPath) switch
        {
            ProfileInputKind.Gcdump => LoadGcdump(inputPath),
            ProfileInputKind.HeapTextReport or ProfileInputKind.HeapLogReport => _parseGcdumpReport(File.ReadAllText(inputPath)),
            _ => WriteUnsupportedInput(inputPath)
        };
    }

    public HeapProfileResult LoadCollectedGcdump(string gcdumpPath)
    {
        return GcdumpReportLoader.Load(
            gcdumpPath,
            _getTheme(),
            _runProcess,
            _parseGcdumpReport,
            _writeLine);
    }

    private HeapProfileResult? LoadGcdump(string inputPath)
    {
        if (!_ensureToolAvailable("dotnet-gcdump", _dotnetGcdumpInstallHint))
        {
            return null;
        }

        return LoadCollectedGcdump(inputPath);
    }

    private HeapProfileResult? WriteUnsupportedInput(string inputPath)
    {
        _reporter.WriteUnsupportedInput("Unsupported heap input", inputPath);
        return null;
    }
}
