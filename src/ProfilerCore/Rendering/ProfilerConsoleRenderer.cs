namespace Asynkron.Profiler;

public sealed class ProfilerConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;
    private readonly CpuConsoleProfileRenderer _cpuRenderer;
    private readonly MemoryConsoleProfileRenderer _memoryRenderer;
    private readonly ExceptionConsoleProfileRenderer _exceptionRenderer;
    private readonly ContentionConsoleProfileRenderer _contentionRenderer;
    private readonly HeapConsoleProfileRenderer _heapRenderer;

    public ProfilerConsoleRenderer(Theme? theme = null)
    {
        _theme = theme ?? Theme.Current;
        var tableWriter = new ProfilerConsoleTableWriter(_theme);
        var callTreeFormatter = new ProfilerCallTreeFormatter(_theme);
        _callTreeRenderer = new ProfilerCallTreeRenderer(_theme, callTreeFormatter);
        _cpuRenderer = new CpuConsoleProfileRenderer(_theme, tableWriter, _callTreeRenderer);
        _memoryRenderer = new MemoryConsoleProfileRenderer(_theme, tableWriter, _callTreeRenderer);
        _exceptionRenderer = new ExceptionConsoleProfileRenderer(_theme, _callTreeRenderer);
        _contentionRenderer = new ContentionConsoleProfileRenderer(_theme, _callTreeRenderer);
        _heapRenderer = new HeapConsoleProfileRenderer(_theme);
    }

    public Theme Theme => _theme;

    public void PrintCpuResults(
        CpuProfileResult? results,
        ProfileRenderRequest request,
        MemoryProfileResult? memoryResults = null)
        => _cpuRenderer.Print(results, request, memoryResults);

    public void PrintMemoryResults(
        MemoryProfileResult? results,
        ProfileRenderRequest request)
        => _memoryRenderer.Print(results, request);

    public void PrintExceptionResults(
        ExceptionProfileResult? results,
        ProfileRenderRequest request)
        => _exceptionRenderer.Print(results, request);

    public void PrintContentionResults(
        ContentionProfileResult? results,
        ProfileRenderRequest request)
        => _contentionRenderer.Print(results, request);

    public void PrintHeapResults(HeapProfileResult? results, ProfileRenderRequest request)
        => _heapRenderer.Print(results, request);

    public string HighlightJitNumbers(string text) => _callTreeRenderer.HighlightJitNumbers(text);
}
