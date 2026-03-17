namespace Asynkron.Profiler;

public sealed class ProfilerConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCpuConsoleRenderer _cpuRenderer;
    private readonly ProfilerMemoryConsoleRenderer _memoryRenderer;
    private readonly ProfilerExceptionConsoleRenderer _exceptionRenderer;
    private readonly ProfilerContentionConsoleRenderer _contentionRenderer;
    private readonly ProfilerHeapConsoleRenderer _heapRenderer;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ProfilerConsoleRenderer(Theme? theme = null)
    {
        _theme = theme ?? Theme.Current;
        var tableWriter = new ProfilerConsoleTableWriter(_theme);
        var callTreeFormatter = new ProfilerCallTreeFormatter(_theme);
        _callTreeRenderer = new ProfilerCallTreeRenderer(_theme, callTreeFormatter);
        _cpuRenderer = new ProfilerCpuConsoleRenderer(_theme, tableWriter, _callTreeRenderer);
        _memoryRenderer = new ProfilerMemoryConsoleRenderer(_theme, tableWriter, _callTreeRenderer);
        _exceptionRenderer = new ProfilerExceptionConsoleRenderer(_theme, _callTreeRenderer);
        _contentionRenderer = new ProfilerContentionConsoleRenderer(_theme, _callTreeRenderer);
        _heapRenderer = new ProfilerHeapConsoleRenderer(_theme);
    }

    public Theme Theme => _theme;

    public void PrintCpuResults(CpuProfileResult? results, ProfileRenderRequest request, MemoryProfileResult? memoryResults = null) => _cpuRenderer.Print(results, request, memoryResults);

    public void PrintMemoryResults(MemoryProfileResult? results, ProfileRenderRequest request) => _memoryRenderer.Print(results, request);

    public void PrintExceptionResults(ExceptionProfileResult? results, ProfileRenderRequest request) => _exceptionRenderer.Print(results, request);

    public void PrintContentionResults(ContentionProfileResult? results, ProfileRenderRequest request) => _contentionRenderer.Print(results, request);

    public void PrintHeapResults(HeapProfileResult? results, ProfileRenderRequest request) => _heapRenderer.Print(results, request);

    public string HighlightJitNumbers(string text) => _callTreeRenderer.HighlightJitNumbers(text);
}
