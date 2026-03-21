using System;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class TraceProfileInputLoader
{
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;
    private readonly Func<Theme> _getTheme;
    private readonly Action<string> _writeLine;

    public TraceProfileInputLoader(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        Action<string> writeLine)
    {
        _traceAnalyzer = traceAnalyzer;
        _getTheme = getTheme;
        _writeLine = writeLine;
    }

    public CpuProfileResult? LoadCpu(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        return ProfileInputKindResolver.Resolve(inputPath) switch
        {
            ProfileInputKind.Speedscope => AnalyzeSpeedscope(inputPath),
            var kind when ProfileInputKindResolver.IsTrace(kind) => AnalyzeCpuTrace(inputPath),
            _ => WriteUnsupportedInput<CpuProfileResult>("Unsupported CPU input", inputPath)
        };
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        var callTree = LoadTraceInput(inputPath, "Unsupported memory input", AnalyzeAllocationTrace);
        return callTree == null ? null : MemoryProfileResultFactory.Create(callTree);
    }

    public ExceptionProfileResult? LoadException(string inputPath)
    {
        return LoadTraceInput(inputPath, "Unsupported exception input", AnalyzeExceptionTrace);
    }

    public ContentionProfileResult? LoadContention(string inputPath)
    {
        return LoadTraceInput(inputPath, "Unsupported contention input", AnalyzeContentionTrace);
    }

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        var result = RunAnalysis(() => _traceAnalyzer.AnalyzeCpuTrace(traceFile), "CPU trace parse failed");
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
        return RunAnalysis(() => _traceAnalyzer.AnalyzeAllocationTrace(traceFile), "Allocation trace parse failed");
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        return RunAnalysis(() => _traceAnalyzer.AnalyzeExceptionTrace(traceFile), "Exception trace parse failed");
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        return RunAnalysis(() => _traceAnalyzer.AnalyzeContentionTrace(traceFile), "Contention trace parse failed");
    }

    private CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
    {
        return RunAnalysis(() => ProfilerTraceAnalyzer.AnalyzeSpeedscope(speedscopePath), "Speedscope parse failed");
    }

    private TResult? LoadTraceInput<TResult>(
        string inputPath,
        string unsupportedMessage,
        Func<string, TResult?> analyzeTrace)
        where TResult : class
    {
        if (!TryValidateTraceInput(inputPath, unsupportedMessage))
        {
            return null;
        }

        return analyzeTrace(inputPath);
    }

    private TResult? RunAnalysis<TResult>(Func<TResult> analyze, string failureLabel)
        where TResult : class
    {
        try
        {
            return analyze();
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]{failureLabel}:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private bool TryValidateTraceInput(string inputPath, string unsupportedMessage)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return false;
        }

        if (ProfileInputKindResolver.IsTrace(ProfileInputKindResolver.Resolve(inputPath)))
        {
            return true;
        }

        WriteUnsupportedInput<object>(unsupportedMessage, inputPath);
        return false;
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

    private TResult? WriteUnsupportedInput<TResult>(string message, string inputPath)
        where TResult : class
    {
        _writeLine($"[{_getTheme().ErrorColor}]{message}:[/] {Markup.Escape(inputPath)}");
        return null;
    }
}
