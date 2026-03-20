using System;
using System.Collections.Generic;
using System.IO;

namespace Asynkron.Profiler;

internal sealed class ProfileInputLoader
{
    private readonly ProfileTraceAnalysisRunner _analysisRunner;
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly Action<string> _writeLine;
    private readonly string _dotnetGcdumpInstallHint;

    public ProfileInputLoader(
        ProfileTraceAnalysisRunner analysisRunner,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        _analysisRunner = analysisRunner;
        _getTheme = getTheme;
        _ensureToolAvailable = ensureToolAvailable;
        _runProcess = runProcess;
        _parseGcdumpReport = parseGcdumpReport;
        _writeLine = writeLine;
        _dotnetGcdumpInstallHint = dotnetGcdumpInstallHint;
    }

    public CpuProfileResult? LoadCpu(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        switch (ProfileInputConventions.Classify(inputPath))
        {
            case ProfileInputKind.Speedscope:
                return _analysisRunner.AnalyzeSpeedscope(inputPath);
            case ProfileInputKind.NetTrace:
            case ProfileInputKind.TraceLog:
                return _analysisRunner.AnalyzeCpuTrace(inputPath);
            default:
                WriteUnsupportedInput("Unsupported CPU input", inputPath);
                return null;
        }
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported memory input"))
        {
            return null;
        }

        var callTree = _analysisRunner.AnalyzeAllocationTrace(inputPath);
        return callTree == null ? null : MemoryProfileResultFactory.Create(callTree);
    }

    public ExceptionProfileResult? LoadException(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported exception input"))
        {
            return null;
        }

        return _analysisRunner.AnalyzeExceptionTrace(inputPath);
    }

    public ContentionProfileResult? LoadContention(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported contention input"))
        {
            return null;
        }

        return _analysisRunner.AnalyzeContentionTrace(inputPath);
    }

    public HeapProfileResult? LoadHeap(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        switch (ProfileInputConventions.Classify(inputPath))
        {
            case ProfileInputKind.GcDump:
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
            case ProfileInputKind.HeapReport:
                return _parseGcdumpReport(File.ReadAllText(inputPath));
            default:
                WriteUnsupportedInput("Unsupported heap input", inputPath);
                return null;
        }
    }

    private bool TryValidateTraceInput(string inputPath, string unsupportedMessage)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return false;
        }

        var inputKind = ProfileInputConventions.Classify(inputPath);
        if (inputKind is ProfileInputKind.NetTrace or ProfileInputKind.TraceLog)
        {
            return true;
        }

        WriteUnsupportedInput(unsupportedMessage, inputPath);
        return false;
    }

    private bool TryEnsureInputExists(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return true;
        }

        _writeLine($"[{_getTheme().ErrorColor}]Input file not found:[/] {Spectre.Console.Markup.Escape(inputPath)}");
        return false;
    }

    private void WriteUnsupportedInput(string message, string inputPath)
    {
        _writeLine($"[{_getTheme().ErrorColor}]{message}:[/] {Spectre.Console.Markup.Escape(inputPath)}");
    }
}
