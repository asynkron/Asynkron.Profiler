using System;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileTraceAnalysisRunner
{
    private readonly Func<Theme> _getTheme;
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;
    private readonly Action<string> _writeLine;

    public ProfileTraceAnalysisRunner(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        Action<string> writeLine)
    {
        _traceAnalyzer = traceAnalyzer;
        _getTheme = getTheme;
        _writeLine = writeLine;
    }

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        return Execute(
            () => _traceAnalyzer.AnalyzeCpuTrace(traceFile),
            "CPU trace parse failed",
            result =>
            {
                if (result.AllFunctions.Count == 0)
                {
                    _writeLine($"[{_getTheme().AccentColor}]No CPU samples found in trace.[/]");
                    return null;
                }

                return result;
            });
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        return Execute(() => _traceAnalyzer.AnalyzeAllocationTrace(traceFile), "Allocation trace parse failed");
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        return Execute(() => _traceAnalyzer.AnalyzeExceptionTrace(traceFile), "Exception trace parse failed");
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        return Execute(() => _traceAnalyzer.AnalyzeContentionTrace(traceFile), "Contention trace parse failed");
    }

    public CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
    {
        return Execute(() => ProfilerTraceAnalyzer.AnalyzeSpeedscope(speedscopePath), "Speedscope parse failed");
    }

    private TResult? Execute<TResult>(
        Func<TResult> analyze,
        string failureLabel,
        Func<TResult, TResult?>? normalize = null)
        where TResult : class
    {
        try
        {
            var result = analyze();
            return normalize?.Invoke(result) ?? result;
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]{failureLabel}:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }
}
