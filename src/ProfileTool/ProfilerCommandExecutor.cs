using System;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerCommandExecutor
{
    private readonly string _outputDirectory;
    private readonly ProfilerExecutionResultResolver _resultResolver;
    private readonly ProfilerExecutionRequestFactory _requestFactory;
    private readonly ProfilerThemeRuntime _themeRuntime;

    public ProfilerCommandExecutor(string workingDirectory)
    {
        ArgumentGuard.RequireNotWhiteSpace(workingDirectory, nameof(workingDirectory), "Working directory is required.");

        _outputDirectory = Path.Combine(workingDirectory, "profile-output");
        Directory.CreateDirectory(_outputDirectory);

        _themeRuntime = new ProfilerThemeRuntime(_outputDirectory, Theme.Current, AnsiConsole.MarkupLine);
        var toolAvailability = new ProfilerToolAvailability(() => _themeRuntime.CurrentTheme, ProcessRunner.Run, AnsiConsole.MarkupLine);

        var traceAnalyzer = new ProfilerTraceAnalyzer(_outputDirectory);
        var profileInputLoader = new ProfileInputLoader(
            traceAnalyzer,
            () => _themeRuntime.CurrentTheme,
            toolAvailability.EnsureAvailable,
            ProcessRunner.Run,
            GcdumpReportParser.Parse,
            AnsiConsole.MarkupLine,
            ProfileCollectionRunner.DotnetGcdumpInstallHint);
        var collectionRunner = new ProfileCollectionRunner(
            _outputDirectory,
            () => _themeRuntime.CurrentTheme,
            toolAvailability.EnsureAvailable,
            ProcessRunner.Run,
            profileInputLoader,
            AnsiConsole.MarkupLine);
        _resultResolver = new ProfilerExecutionResultResolver(collectionRunner, profileInputLoader);
        _requestFactory = new ProfilerExecutionRequestFactory(() => _themeRuntime.CurrentTheme, ProcessRunner.Run);
    }

    public void Execute(ProfilerCommandInvocation invocation)
    {
        if (!_themeRuntime.TryApply(invocation.ThemeName))
        {
            return;
        }

        var request = _requestFactory.TryCreate(invocation);
        if (request == null)
        {
            return;
        }

        if (_themeRuntime.JitModeRunner.TryHandleDumpModes(request))
        {
            return;
        }

        if (!_themeRuntime.JitModeRunner.ValidateHotJitRequest(request))
        {
            return;
        }

        var sharedTraceFile = _resultResolver.CollectSharedTraceFile(request);
        if (request.RunCpu && request.RunMemory)
        {
            ExecuteCombinedCpuAndMemory(request, sharedTraceFile);
        }
        else
        {
            ExecuteIndependentCpuAndMemory(request, sharedTraceFile);
        }

        if (request.RunException)
        {
            Console.WriteLine($"{request.Label} - exception");
            var results = _resultResolver.ResolveExceptionResults(request, sharedTraceFile);
            _themeRuntime.Renderer.PrintExceptionResults(results, request.RenderRequest);
        }

        if (request.RunContention)
        {
            Console.WriteLine($"{request.Label} - contention");
            var results = _resultResolver.ResolveContentionResults(request);
            _themeRuntime.Renderer.PrintContentionResults(results, request.RenderRequest);
        }

        if (request.RunHeap)
        {
            Console.WriteLine($"{request.Label} - heap");
            var results = _resultResolver.ResolveHeapResults(request);
            _themeRuntime.Renderer.PrintHeapResults(results, request.RenderRequest);
        }
    }

    private void RenderCpuResults(
        ProfilerExecutionRequest request,
        CpuProfileResult? results,
        MemoryProfileResult? memoryResults = null)
    {
        _themeRuntime.Renderer.PrintCpuResults(results, request.RenderRequest, memoryResults);

        if (results != null && request.ShouldRunHotJitDisasm)
        {
            _themeRuntime.HotJitDisasmRunner.Run(request, results);
        }
    }

    private void ExecuteCombinedCpuAndMemory(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        Console.WriteLine($"{request.Label} - cpu+memory");
        var cpuResults = _resultResolver.ResolveCpuResults(request, sharedTraceFile);
        var memoryResults = _resultResolver.ResolveMemoryResults(request, sharedTraceFile);

        if (cpuResults != null)
        {
            RenderCpuResults(request, cpuResults, memoryResults);
        }
        else if (memoryResults != null)
        {
            _themeRuntime.Renderer.PrintMemoryResults(memoryResults, request.RenderRequest);
        }
    }

    private void ExecuteIndependentCpuAndMemory(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.RunCpu)
        {
            Console.WriteLine($"{request.Label} - cpu");
            var results = _resultResolver.ResolveCpuResults(request, sharedTraceFile);
            RenderCpuResults(request, results);
        }

        if (request.RunMemory)
        {
            Console.WriteLine($"{request.Label} - memory");
            var results = _resultResolver.ResolveMemoryResults(request, sharedTraceFile);
            _themeRuntime.Renderer.PrintMemoryResults(results, request.RenderRequest);
        }
    }
}
