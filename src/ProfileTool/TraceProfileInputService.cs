using System;

namespace Asynkron.Profiler;

internal sealed class TraceProfileInputService
{
    private readonly ProfileLoadReporter _reporter;
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;

    public TraceProfileInputService(ProfilerTraceAnalyzer traceAnalyzer, ProfileLoadReporter reporter)
    {
        _traceAnalyzer = traceAnalyzer;
        _reporter = reporter;
    }

    public CpuProfileResult? LoadCpu(string inputPath)
    {
        if (!ProfileInputPathValidator.TryEnsureExists(inputPath, _reporter))
        {
            return null;
        }

        var kind = ProfileInputCatalog.GetKind(inputPath);
        if (kind == ProfileInputKind.SpeedscopeJson)
        {
            return AnalyzeSpeedscope(inputPath);
        }

        if (ProfileInputCatalog.IsTraceInput(kind))
        {
            return AnalyzeCpuTrace(inputPath);
        }

        _reporter.WriteUnsupportedInput("Unsupported CPU input", inputPath);
        return null;
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported memory input"))
        {
            return null;
        }

        var callTree = AnalyzeAllocationTrace(inputPath);
        return callTree == null ? null : MemoryProfileResultFactory.Create(callTree);
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
        try
        {
            var result = _traceAnalyzer.AnalyzeCpuTrace(traceFile);
            if (result.AllFunctions.Count == 0)
            {
                _reporter.WriteNoCpuSamplesFound();
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _reporter.WriteParseFailure("CPU trace parse failed", ex);
            return null;
        }
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        try
        {
            return _traceAnalyzer.AnalyzeAllocationTrace(traceFile);
        }
        catch (Exception ex)
        {
            _reporter.WriteParseFailure("Allocation trace parse failed", ex);
            return null;
        }
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        try
        {
            return _traceAnalyzer.AnalyzeExceptionTrace(traceFile);
        }
        catch (Exception ex)
        {
            _reporter.WriteParseFailure("Exception trace parse failed", ex);
            return null;
        }
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        try
        {
            return _traceAnalyzer.AnalyzeContentionTrace(traceFile);
        }
        catch (Exception ex)
        {
            _reporter.WriteParseFailure("Contention trace parse failed", ex);
            return null;
        }
    }

    private CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
    {
        try
        {
            return ProfilerTraceAnalyzer.AnalyzeSpeedscope(speedscopePath);
        }
        catch (Exception ex)
        {
            _reporter.WriteParseFailure("Speedscope parse failed", ex);
            return null;
        }
    }

    private bool TryValidateTraceInput(string inputPath, string unsupportedMessage)
    {
        if (!ProfileInputPathValidator.TryEnsureExists(inputPath, _reporter))
        {
            return false;
        }

        if (ProfileInputCatalog.IsTraceInput(ProfileInputCatalog.GetKind(inputPath)))
        {
            return true;
        }

        _reporter.WriteUnsupportedInput(unsupportedMessage, inputPath);
        return false;
    }
}
