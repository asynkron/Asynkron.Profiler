using System;
using System.IO;

namespace Asynkron.Profiler;

public sealed class ProfilerAnalysisException : Exception
{
    public ProfilerAnalysisException(string message) : base(message)
    {
    }

    public ProfilerAnalysisException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class ProfilerTraceAnalyzer
{
    public ProfilerTraceAnalyzer(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        OutputDirectory = outputDirectory;
        Directory.CreateDirectory(OutputDirectory);
    }

    public string OutputDirectory { get; }

    public static CpuProfileResult AnalyzeSpeedscope(string speedscopePath)
    {
        if (string.IsNullOrWhiteSpace(speedscopePath))
        {
            throw new ArgumentException("Speedscope path is required.", nameof(speedscopePath));
        }

        if (!File.Exists(speedscopePath))
        {
            throw new FileNotFoundException("Speedscope file not found.", speedscopePath);
        }

        var result = SpeedscopeParser.ParseFile(speedscopePath);
        if (result == null)
        {
            throw new ProfilerAnalysisException("Speedscope parse failed.");
        }

        return result;
    }

    public CpuProfileResult AnalyzeCpuTrace(string traceFile)
    {
        return AnalyzeTrace(traceFile, "CPU trace parse failed", CpuTraceAnalyzer.Analyze);
    }

    public AllocationCallTreeResult AnalyzeAllocationTrace(string traceFile)
    {
        return AnalyzeTrace(traceFile, "Allocation trace parse failed", AllocationTraceAnalyzer.Analyze);
    }

    public ExceptionProfileResult AnalyzeExceptionTrace(string traceFile)
    {
        return AnalyzeTrace(traceFile, "Exception trace parse failed", ExceptionTraceAnalyzer.Analyze);
    }

    public ContentionProfileResult AnalyzeContentionTrace(string traceFile)
    {
        return AnalyzeTrace(traceFile, "Contention trace parse failed", ContentionTraceAnalyzer.Analyze);
    }

    private T AnalyzeTrace<T>(string traceFile, string failureLabel, Func<string, string, T> analyze)
    {
        if (!File.Exists(traceFile))
        {
            throw new FileNotFoundException("Trace file not found.", traceFile);
        }

        try
        {
            return analyze(traceFile, OutputDirectory);
        }
        catch (ProfilerAnalysisException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProfilerAnalysisException($"{failureLabel}: {ex.Message}", ex);
        }
    }
}
