using System;
using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed class ProfileInputLoader
{
    private readonly TraceProfileInputLoader _traceInputLoader;
    private readonly HeapProfileInputLoader _heapInputLoader;

    public ProfileInputLoader(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        _traceInputLoader = new TraceProfileInputLoader(traceAnalyzer, getTheme, writeLine);
        _heapInputLoader = new HeapProfileInputLoader(
            getTheme,
            ensureToolAvailable,
            runProcess,
            parseGcdumpReport,
            writeLine,
            dotnetGcdumpInstallHint);
    }

    public CpuProfileResult? LoadCpu(string inputPath) => _traceInputLoader.LoadCpu(inputPath);

    public MemoryProfileResult? LoadMemory(string inputPath) => _traceInputLoader.LoadMemory(inputPath);

    public ExceptionProfileResult? LoadException(string inputPath) => _traceInputLoader.LoadException(inputPath);

    public ContentionProfileResult? LoadContention(string inputPath) => _traceInputLoader.LoadContention(inputPath);

    public HeapProfileResult? LoadHeap(string inputPath) => _heapInputLoader.Load(inputPath);

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile) => _traceInputLoader.AnalyzeCpuTrace(traceFile);

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile) => _traceInputLoader.AnalyzeAllocationTrace(traceFile);

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile) => _traceInputLoader.AnalyzeExceptionTrace(traceFile);

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile) => _traceInputLoader.AnalyzeContentionTrace(traceFile);
}
