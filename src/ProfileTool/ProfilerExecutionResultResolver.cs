namespace Asynkron.Profiler;

internal sealed class ProfilerExecutionResultResolver
{
    private readonly ProfileCollectionRunner _collectionRunner;
    private readonly ProfileInputLoader _profileInputLoader;

    public ProfilerExecutionResultResolver(ProfileCollectionRunner collectionRunner, ProfileInputLoader profileInputLoader)
    {
        _collectionRunner = collectionRunner;
        _profileInputLoader = profileInputLoader;
    }

    public string? CollectSharedTraceFile(ProfilerExecutionRequest request)
    {
        if (request.HasInput || !request.RunCpu || (!request.RunMemory && !request.RunException))
        {
            return null;
        }

        return _collectionRunner.CollectCpuTrace(request.Command, request.Label, request.RunMemory, request.RunException);
    }

    public CpuProfileResult? ResolveCpuResults(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.HasInput)
        {
            return _profileInputLoader.LoadCpu(request.InputPath!);
        }

        return sharedTraceFile != null
            ? _profileInputLoader.AnalyzeCpuTrace(sharedTraceFile)
            : _collectionRunner.RunCpuProfile(request.Command, request.Label);
    }

    public MemoryProfileResult? ResolveMemoryResults(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.HasInput)
        {
            return _profileInputLoader.LoadMemory(request.InputPath!);
        }

        return sharedTraceFile != null
            ? _profileInputLoader.LoadMemory(sharedTraceFile)
            : _collectionRunner.RunMemoryProfile(request.Command, request.Label);
    }

    public ExceptionProfileResult? ResolveExceptionResults(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.HasInput)
        {
            return _profileInputLoader.LoadException(request.InputPath!);
        }

        return sharedTraceFile != null
            ? _profileInputLoader.LoadException(sharedTraceFile)
            : _collectionRunner.RunExceptionProfile(request.Command, request.Label);
    }

    public ContentionProfileResult? ResolveContentionResults(ProfilerExecutionRequest request)
    {
        return request.HasInput
            ? _profileInputLoader.LoadContention(request.InputPath!)
            : _collectionRunner.RunContentionProfile(request.Command, request.Label);
    }

    public HeapProfileResult? ResolveHeapResults(ProfilerExecutionRequest request)
    {
        return request.HasInput
            ? _profileInputLoader.LoadHeap(request.InputPath!)
            : _collectionRunner.RunHeapProfile(request.Command, request.Label);
    }
}
