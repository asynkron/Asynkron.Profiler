using System;
using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerCommandExecutor
{
    private readonly ProfileCollectionRunner _collectionRunner;
    private readonly string _outputDirectory;
    private readonly ProfileInputLoader _profileInputLoader;
    private readonly ProfilerExecutionRequestFactory _requestFactory;
    private readonly ProfilerToolAvailability _toolAvailability;

    private HotJitDisasmRunner _hotJitDisasmRunner = null!;
    private ProfilerJitModeRunner _jitModeRunner = null!;
    private ProfilerConsoleRenderer _renderer = null!;
    private Theme _theme;

    public ProfilerCommandExecutor(string workingDirectory)
    {
        ArgumentGuard.RequireNotWhiteSpace(workingDirectory, nameof(workingDirectory), "Working directory is required.");

        _theme = Theme.Current;
        _outputDirectory = Path.Combine(workingDirectory, "profile-output");
        Directory.CreateDirectory(_outputDirectory);

        RefreshThemeServices();
        _toolAvailability = new ProfilerToolAvailability(() => _theme, ProcessRunner.Run, AnsiConsole.MarkupLine);

        var traceAnalyzer = new ProfilerTraceAnalyzer(_outputDirectory);
        _profileInputLoader = new ProfileInputLoader(
            traceAnalyzer,
            () => _theme,
            _toolAvailability.EnsureAvailable,
            ProcessRunner.Run,
            GcdumpReportParser.Parse,
            AnsiConsole.MarkupLine,
            ProfileCollectionRunner.DotnetGcdumpInstallHint);
        _collectionRunner = new ProfileCollectionRunner(
            _outputDirectory,
            () => _theme,
            _toolAvailability.EnsureAvailable,
            ProcessRunner.Run,
            _profileInputLoader,
            AnsiConsole.MarkupLine);
        _requestFactory = new ProfilerExecutionRequestFactory(() => _theme, ProcessRunner.Run);
    }

    public void Execute(ProfilerCommandInvocation invocation)
    {
        if (!TryApplyTheme(invocation.ThemeName))
        {
            return;
        }

        var request = _requestFactory.TryCreate(invocation);
        if (request == null)
        {
            return;
        }

        if (_jitModeRunner.TryHandleDumpModes(request))
        {
            return;
        }

        if (!_jitModeRunner.ValidateHotJitRequest(request))
        {
            return;
        }

        var sharedTraceFile = CollectSharedTraceFile(request);
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
            var results = ResolveExceptionResults(request, sharedTraceFile);
            _renderer.PrintExceptionResults(results, request.RenderRequest);
        }

        if (request.RunContention)
        {
            Console.WriteLine($"{request.Label} - contention");
            var results = ResolveContentionResults(request);
            _renderer.PrintContentionResults(results, request.RenderRequest);
        }

        if (request.RunHeap)
        {
            Console.WriteLine($"{request.Label} - heap");
            var results = ResolveHeapResults(request);
            _renderer.PrintHeapResults(results, request.RenderRequest);
        }
    }

    private void RenderCpuResults(
        ProfilerExecutionRequest request,
        CpuProfileResult? results,
        MemoryProfileResult? memoryResults = null)
    {
        _renderer.PrintCpuResults(results, request.RenderRequest, memoryResults);

        if (results != null && request.ShouldRunHotJitDisasm)
        {
            _hotJitDisasmRunner.Run(request, results);
        }
    }

    private void ExecuteCombinedCpuAndMemory(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        Console.WriteLine($"{request.Label} - cpu+memory");
        var cpuResults = ResolveCpuResults(request, sharedTraceFile);
        var memoryResults = ResolveMemoryResults(request, sharedTraceFile);

        if (cpuResults != null)
        {
            RenderCpuResults(request, cpuResults, memoryResults);
        }
        else if (memoryResults != null)
        {
            _renderer.PrintMemoryResults(memoryResults, request.RenderRequest);
        }
    }

    private void ExecuteIndependentCpuAndMemory(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.RunCpu)
        {
            Console.WriteLine($"{request.Label} - cpu");
            var results = ResolveCpuResults(request, sharedTraceFile);
            RenderCpuResults(request, results);
        }

        if (request.RunMemory)
        {
            Console.WriteLine($"{request.Label} - memory");
            var results = ResolveMemoryResults(request, sharedTraceFile);
            _renderer.PrintMemoryResults(results, request.RenderRequest);
        }
    }

    private CpuProfileResult? ResolveCpuResults(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.HasInput)
        {
            return _profileInputLoader.LoadCpu(request.InputPath!);
        }

        return sharedTraceFile != null
            ? _profileInputLoader.AnalyzeCpuTrace(sharedTraceFile)
            : _collectionRunner.RunCpuProfile(request.Command, request.Label);
    }

    private MemoryProfileResult? ResolveMemoryResults(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.HasInput)
        {
            return _profileInputLoader.LoadMemory(request.InputPath!);
        }

        return sharedTraceFile != null
            ? _profileInputLoader.LoadMemory(sharedTraceFile)
            : _collectionRunner.RunMemoryProfile(request.Command, request.Label);
    }

    private ExceptionProfileResult? ResolveExceptionResults(ProfilerExecutionRequest request, string? sharedTraceFile)
    {
        if (request.HasInput)
        {
            return _profileInputLoader.LoadException(request.InputPath!);
        }

        return sharedTraceFile != null
            ? _profileInputLoader.LoadException(sharedTraceFile)
            : _collectionRunner.RunExceptionProfile(request.Command, request.Label);
    }

    private ContentionProfileResult? ResolveContentionResults(ProfilerExecutionRequest request)
    {
        return request.HasInput
            ? _profileInputLoader.LoadContention(request.InputPath!)
            : _collectionRunner.RunContentionProfile(request.Command, request.Label);
    }

    private HeapProfileResult? ResolveHeapResults(ProfilerExecutionRequest request)
    {
        return request.HasInput
            ? _profileInputLoader.LoadHeap(request.InputPath!)
            : _collectionRunner.RunHeapProfile(request.Command, request.Label);
    }

    private string? CollectSharedTraceFile(ProfilerExecutionRequest request)
    {
        if (request.HasInput || !request.RunCpu || (!request.RunMemory && !request.RunException))
        {
            return null;
        }

        return _collectionRunner.CollectCpuTrace(request.Command, request.Label, request.RunMemory, request.RunException);
    }

    private bool TryApplyTheme(string? themeName)
    {
        if (!Theme.TryResolve(themeName, out var selectedTheme))
        {
            var name = themeName ?? string.Empty;
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Unknown theme '{Markup.Escape(name)}'[/]");
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]Available themes:[/] {Theme.AvailableThemes}");
            return false;
        }

        Theme.Current = selectedTheme;
        _theme = selectedTheme;
        RefreshThemeServices();
        return true;
    }

    private void RefreshThemeServices()
    {
        _renderer = new ProfilerConsoleRenderer(_theme);
        var jitOutputFormatter = new JitOutputFormatter(_theme);
        var jitCommandRunner = new JitCommandRunner(_theme, _outputDirectory, jitOutputFormatter);
        var jitContext = new JitExecutionContext(_theme, jitCommandRunner, jitOutputFormatter, WriteOutputFiles);
        _jitModeRunner = new ProfilerJitModeRunner(jitContext);
        _hotJitDisasmRunner = new HotJitDisasmRunner(jitContext);
    }

    private void WriteOutputFiles(string label, IEnumerable<string> files)
    {
        AnsiConsole.MarkupLine($"[{_theme.AccentColor}]{label}:[/]");
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }
    }
}
