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
    private readonly TraceFileLocator _traceFileLocator;

    public ProfilerTraceAnalyzer(string outputDirectory)
    {
        OutputDirectory = ArgumentGuard.RequireNotWhiteSpace(outputDirectory, nameof(outputDirectory), "Output directory is required.");
        Directory.CreateDirectory(OutputDirectory);
        _traceFileLocator = new TraceFileLocator(OutputDirectory);
    }

    public string OutputDirectory { get; }

    public static CpuProfileResult AnalyzeSpeedscope(string speedscopePath)
    {
        speedscopePath = ArgumentGuard.RequireExistingFile(
            speedscopePath,
            nameof(speedscopePath),
            "Speedscope path is required.",
            "Speedscope file not found.");
        var result = SpeedscopeParser.ParseFile(speedscopePath);
        if (result == null)
        {
            throw new ProfilerAnalysisException("Speedscope parse failed.");
        }

        return result;
    }

    public CpuProfileResult AnalyzeCpuTrace(string traceFile)
    {
        return CpuTraceAnalyzer.Analyze(ResolveTracePath(traceFile), traceFile);
    }

    public AllocationCallTreeResult AnalyzeAllocationTrace(string traceFile)
    {
        return AllocationTraceAnalyzer.Analyze(ResolveTracePath(traceFile));
    }

    public ExceptionProfileResult AnalyzeExceptionTrace(string traceFile)
    {
        return ExceptionTraceAnalyzer.Analyze(ResolveTracePath(traceFile));
    }

    public ContentionProfileResult AnalyzeContentionTrace(string traceFile)
    {
        return ContentionTraceAnalyzer.Analyze(ResolveTracePath(traceFile));
    }

    private string ResolveTracePath(string traceFile)
    {
        traceFile = ArgumentGuard.RequireExistingFile(
            traceFile,
            nameof(traceFile),
            "Trace file is required.",
            "Trace file not found.");
        return _traceFileLocator.ResolveEtlxPath(traceFile);
    }
}
