using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileInputLoader
{
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly Action<string> _writeLine;
    private readonly string _dotnetGcdumpInstallHint;

    public ProfileInputLoader(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        _traceAnalyzer = traceAnalyzer;
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

        return ProfileInputPath.GetKind(inputPath) switch
        {
            ProfileInputKind.Speedscope => AnalyzeSpeedscope(inputPath),
            ProfileInputKind.NetTrace or ProfileInputKind.Etlx => AnalyzeCpuTrace(inputPath),
            _ => WriteUnsupportedInputAndReturn<CpuProfileResult>("Unsupported CPU input", inputPath)
        };
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported memory input"))
        {
            return null;
        }

        var callTree = AnalyzeAllocationTrace(inputPath);
        return callTree == null ? null : MemoryProfileResultFactory.Build(callTree);
    }

    public ExceptionProfileResult? LoadException(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported exception input"))
        {
            return null;
        }

        return AnalyzeExceptionTrace(inputPath);
    }

    public ContentionProfileResult? LoadContention(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported contention input"))
        {
            return null;
        }

        return AnalyzeContentionTrace(inputPath);
    }

    public HeapProfileResult? LoadHeap(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        return ProfileInputPath.GetKind(inputPath) switch
        {
            ProfileInputKind.Gcdump => LoadGcdump(inputPath),
            ProfileInputKind.HeapReport => _parseGcdumpReport(File.ReadAllText(inputPath)),
            _ => WriteUnsupportedInputAndReturn<HeapProfileResult>("Unsupported heap input", inputPath)
        };
    }

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        var result = TryAnalyzeTrace(traceFile, "CPU trace parse failed", _traceAnalyzer.AnalyzeCpuTrace);
        if (result == null)
        {
            return null;
        }

        if (result.AllFunctions.Count == 0)
        {
            _writeLine($"[{_getTheme().AccentColor}]No CPU samples found in trace.[/]");
            return null;
        }

        return result;
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        return TryAnalyzeTrace(traceFile, "Allocation trace parse failed", _traceAnalyzer.AnalyzeAllocationTrace);
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        return TryAnalyzeTrace(traceFile, "Exception trace parse failed", _traceAnalyzer.AnalyzeExceptionTrace);
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        return TryAnalyzeTrace(traceFile, "Contention trace parse failed", _traceAnalyzer.AnalyzeContentionTrace);
    }

    private CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
    {
        return TryAnalyzeTrace(speedscopePath, "Speedscope parse failed", ProfilerTraceAnalyzer.AnalyzeSpeedscope);
    }

    private bool TryValidateTraceInput(string inputPath, string unsupportedMessage)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return false;
        }

        if (ProfileInputPath.IsTraceInput(inputPath))
        {
            return true;
        }

        WriteUnsupportedInput(unsupportedMessage, inputPath);
        return false;
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

    private void WriteUnsupportedInput(string message, string inputPath)
    {
        _writeLine($"[{_getTheme().ErrorColor}]{message}:[/] {Markup.Escape(inputPath)}");
    }

    private T? TryAnalyzeTrace<T>(string path, string failureMessage, Func<string, T> analyze)
        where T : class
    {
        try
        {
            return analyze(path);
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]{failureMessage}[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private T? WriteUnsupportedInputAndReturn<T>(string message, string inputPath)
        where T : class
    {
        WriteUnsupportedInput(message, inputPath);
        return null;
    }
}
