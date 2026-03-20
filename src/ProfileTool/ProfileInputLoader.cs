namespace Asynkron.Profiler;

internal sealed class ProfileInputLoader
{
    private readonly HeapProfileInputLoader _heapLoader;
    private readonly ProfileTraceLoader _traceLoader;

    public ProfileInputLoader(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        var diagnostics = new ProfileInputDiagnostics(getTheme, writeLine);
        _traceLoader = new ProfileTraceLoader(traceAnalyzer, getTheme, diagnostics, writeLine);
        _heapLoader = new HeapProfileInputLoader(
            getTheme,
            diagnostics,
            ensureToolAvailable,
            runProcess,
            parseGcdumpReport,
            writeLine,
            dotnetGcdumpInstallHint);
    }

    public CpuProfileResult? LoadCpu(string inputPath)
    {
        return _traceLoader.LoadCpu(inputPath);
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        return _traceLoader.LoadMemory(inputPath);
    }

    public ExceptionProfileResult? LoadException(string inputPath)
    {
        return _traceLoader.LoadException(inputPath);
    }

    public ContentionProfileResult? LoadContention(string inputPath)
    {
        return _traceLoader.LoadContention(inputPath);
    }

    public HeapProfileResult? LoadHeap(string inputPath)
    {
        return _heapLoader.Load(inputPath);
    }

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        return _traceLoader.AnalyzeCpuTrace(traceFile);
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        return _traceLoader.AnalyzeAllocationTrace(traceFile);
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        return _traceLoader.AnalyzeExceptionTrace(traceFile);
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        return _traceLoader.AnalyzeContentionTrace(traceFile);
    }
}
