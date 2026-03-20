using System;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileTraceLoader
{
    private readonly ProfileInputDiagnostics _diagnostics;
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;
    private readonly Func<Theme> _getTheme;
    private readonly Action<string> _writeLine;

    public ProfileTraceLoader(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        ProfileInputDiagnostics diagnostics,
        Action<string> writeLine)
    {
        _diagnostics = diagnostics;
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

        var extension = ProfileInputConventions.GetNormalizedExtension(inputPath);
        if (extension == ".json")
        {
            return AnalyzeSpeedscope(inputPath);
        }

        if (!ProfileInputConventions.IsTraceExtension(extension))
        {
            _diagnostics.WriteUnsupportedInput("Unsupported CPU input", inputPath);
            return null;
        }

        return AnalyzeCpuTrace(inputPath);
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported memory input"))
        {
            return null;
        }

        var callTree = AnalyzeAllocationTrace(inputPath);
        return callTree == null ? null : ProfileMemoryResultFactory.Build(callTree);
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

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        var result = ExecuteAnalysis("CPU trace parse failed", () => _traceAnalyzer.AnalyzeCpuTrace(traceFile));
        if (result?.AllFunctions.Count == 0)
        {
            _writeLine($"[{_getTheme().AccentColor}]No CPU samples found in trace.[/]");
            return null;
        }

        return result;
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        return ExecuteAnalysis("Allocation trace parse failed", () => _traceAnalyzer.AnalyzeAllocationTrace(traceFile));
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        return ExecuteAnalysis("Exception trace parse failed", () => _traceAnalyzer.AnalyzeExceptionTrace(traceFile));
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        return ExecuteAnalysis("Contention trace parse failed", () => _traceAnalyzer.AnalyzeContentionTrace(traceFile));
    }

    private CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
    {
        return ExecuteAnalysis("Speedscope parse failed", () => ProfilerTraceAnalyzer.AnalyzeSpeedscope(speedscopePath));
    }

    private T? ExecuteAnalysis<T>(string failureLabel, Func<T> analyze)
        where T : class
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

        if (ProfileInputConventions.IsTraceExtension(ProfileInputConventions.GetNormalizedExtension(inputPath)))
        {
            return true;
        }

        _diagnostics.WriteUnsupportedInput(unsupportedMessage, inputPath);
        return false;
    }

    private bool TryEnsureInputExists(string inputPath)
    {
        return _diagnostics.TryEnsureInputExists(inputPath);
    }
}
