using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerCommandExecutor
{
    private readonly ProfileCollectionRunner _collectionRunner;
    private readonly string _outputDirectory;
    private readonly ProfileInputLoader _profileInputLoader;
    private readonly ProfilerToolAvailability _toolAvailability;

    private JitCommandRunner _jitCommandRunner;
    private JitOutputFormatter _jitOutputFormatter;
    private ProfilerConsoleRenderer _renderer;
    private Theme _theme;

    public ProfilerCommandExecutor(string workingDirectory)
    {
        ArgumentGuard.RequireNotWhiteSpace(workingDirectory, nameof(workingDirectory), "Working directory is required.");

        _theme = Theme.Current;
        _outputDirectory = Path.Combine(workingDirectory, "profile-output");
        Directory.CreateDirectory(_outputDirectory);

        _renderer = new ProfilerConsoleRenderer(_theme);
        _jitOutputFormatter = new JitOutputFormatter(_theme);
        _jitCommandRunner = new JitCommandRunner(_theme, _outputDirectory, _jitOutputFormatter);
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
    }

    public void Execute(ProfilerCommandInvocation invocation)
    {
        if (!TryApplyTheme(invocation.ThemeName))
        {
            return;
        }

        var request = TryBuildExecutionRequest(invocation);
        if (request == null)
        {
            return;
        }

        if (TryHandleJitDumpModes(request))
        {
            return;
        }

        if (!ValidateHotJitRequest(request))
        {
            return;
        }

        var sharedTraceFile = CollectSharedTraceFile(request);
        if (request.RunCpu && request.RunMemory)
        {
            Console.WriteLine($"{request.Label} - cpu+memory");
            var cpuResults = request.HasInput
                ? _profileInputLoader.LoadCpu(request.InputPath!)
                : sharedTraceFile != null
                    ? _profileInputLoader.AnalyzeCpuTrace(sharedTraceFile)
                    : _collectionRunner.RunCpuProfile(request.Command, request.Label);
            var memoryResults = request.HasInput
                ? _profileInputLoader.LoadMemory(request.InputPath!)
                : sharedTraceFile != null
                    ? _profileInputLoader.LoadMemory(sharedTraceFile)
                    : _collectionRunner.RunMemoryProfile(request.Command, request.Label);

            if (cpuResults != null)
            {
                RenderCpuResults(request, cpuResults, memoryResults);
            }
            else if (memoryResults != null)
            {
                _renderer.PrintMemoryResults(memoryResults, request.RenderRequest);
            }
        }
        else
        {
            if (request.RunCpu)
            {
                Console.WriteLine($"{request.Label} - cpu");
                var results = request.HasInput
                    ? _profileInputLoader.LoadCpu(request.InputPath!)
                    : sharedTraceFile != null
                        ? _profileInputLoader.AnalyzeCpuTrace(sharedTraceFile)
                        : _collectionRunner.RunCpuProfile(request.Command, request.Label);
                RenderCpuResults(request, results);
            }

            if (request.RunMemory)
            {
                Console.WriteLine($"{request.Label} - memory");
                var results = request.HasInput
                    ? _profileInputLoader.LoadMemory(request.InputPath!)
                    : sharedTraceFile != null
                        ? _profileInputLoader.LoadMemory(sharedTraceFile)
                        : _collectionRunner.RunMemoryProfile(request.Command, request.Label);
                _renderer.PrintMemoryResults(results, request.RenderRequest);
            }
        }

        if (request.RunException)
        {
            Console.WriteLine($"{request.Label} - exception");
            var results = request.HasInput
                ? _profileInputLoader.LoadException(request.InputPath!)
                : sharedTraceFile != null
                    ? _profileInputLoader.LoadException(sharedTraceFile)
                    : _collectionRunner.RunExceptionProfile(request.Command, request.Label);
            _renderer.PrintExceptionResults(results, request.RenderRequest);
        }

        if (request.RunContention)
        {
            Console.WriteLine($"{request.Label} - contention");
            var results = request.HasInput
                ? _profileInputLoader.LoadContention(request.InputPath!)
                : _collectionRunner.RunContentionProfile(request.Command, request.Label);
            _renderer.PrintContentionResults(results, request.RenderRequest);
        }

        if (request.RunHeap)
        {
            Console.WriteLine($"{request.Label} - heap");
            var results = request.HasInput
                ? _profileInputLoader.LoadHeap(request.InputPath!)
                : _collectionRunner.RunHeapProfile(request.Command, request.Label);
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
            RunHotJitDisasm(request, results);
        }
    }

    private ProfilerExecutionRequest? TryBuildExecutionRequest(ProfilerCommandInvocation invocation)
    {
        var hasInput = !string.IsNullOrWhiteSpace(invocation.InputPath);
        var hasExplicitModes = invocation.Cpu || invocation.Memory || invocation.Heap || invocation.Contention || invocation.Exception;
        var runCpu = invocation.Cpu || !hasExplicitModes;
        var runMemory = invocation.Memory || !hasExplicitModes;
        var runHeap = invocation.Heap;
        var runException = invocation.Exception;
        var runContention = invocation.Contention;

        if (invocation.JitDisasmHot || invocation.HotThresholdSpecified)
        {
            runCpu = true;
        }

        string label;
        string description;
        string[] command;

        if (hasInput)
        {
            label = ProfileInputLoader.BuildInputLabel(invocation.InputPath!);
            description = invocation.InputPath!;
            command = Array.Empty<string>();

            if (!hasExplicitModes)
            {
                ProfileInputLoader.ApplyInputDefaults(
                    invocation.InputPath!,
                    ref runCpu,
                    ref runMemory,
                    ref runHeap,
                    ref runException,
                    ref runContention);
            }
        }
        else
        {
            if (invocation.Command.Length == 0)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No command provided.[/]");
                ProfilerCommandLineHelp.WriteUsageExamples(Console.Out);
                return null;
            }

            var resolver = new ProjectResolver(ProcessRunner.Run);
            var resolved = resolver.Resolve(invocation.Command, invocation.TargetFramework);
            if (resolved == null)
            {
                return null;
            }

            command = resolved.Command;
            label = resolved.Label;
            description = resolved.Description;
        }

        var renderRequest = new ProfileRenderRequest(
            label,
            description,
            invocation.CallTreeRoot,
            invocation.FunctionFilter,
            invocation.ExceptionTypeFilter,
            invocation.IncludeRuntime,
            invocation.CallTreeDepth,
            invocation.CallTreeWidth,
            invocation.CallTreeRootMode,
            invocation.CallTreeSelf,
            invocation.CallTreeSiblingCutoff,
            invocation.HotThreshold,
            invocation.Timeline,
            invocation.TimelineWidth);

        return new ProfilerExecutionRequest(
            label,
            description,
            command,
            invocation.InputPath,
            hasInput,
            hasExplicitModes,
            runCpu,
            runMemory,
            runHeap,
            runException,
            runContention,
            invocation.JitInline,
            invocation.JitDisasm,
            invocation.JitDisasmHot,
            invocation.Jit,
            invocation.HotThresholdSpecified,
            invocation.JitMethod,
            invocation.JitAltJitPath,
            invocation.JitAltJitName,
            renderRequest);
    }

    private bool TryHandleJitDumpModes(ProfilerExecutionRequest request)
    {
        if (!request.JitInline && !request.JitDisasm)
        {
            return false;
        }

        if (request.HasInput)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]JIT dump modes require a command, not --input.[/]");
            return true;
        }

        if (request.HasExplicitModes)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]JIT dump modes cannot be combined with other profiling modes.[/]");
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.JitMethod))
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Missing --jit-method (e.g. Namespace.Type:Method).[/]");
            return true;
        }

        if (request.JitInline && request.JitDisasm)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Choose either --jit-inline or --jit-disasm, not both.[/]");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.JitAltJitPath) && !File.Exists(request.JitAltJitPath))
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]AltJit path not found:[/] {Markup.Escape(request.JitAltJitPath)}");
            return true;
        }

        var dumpFiles = request.JitInline
            ? _jitCommandRunner.RunInlineDump(request.Command, request.JitMethod!, request.JitAltJitPath, request.JitAltJitName)
            : _jitCommandRunner.RunDisasm(request.Command, request.JitMethod!);
        WriteOutputFiles(request.JitInline ? "JIT inline dump files" : "JIT disasm files", dumpFiles);

        var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            if (request.JitInline)
            {
                _jitOutputFormatter.PrintInlineSummary(logPath);
            }
            else
            {
                _jitOutputFormatter.PrintDisasmSummary(logPath);
            }
        }

        return true;
    }

    private bool ValidateHotJitRequest(ProfilerExecutionRequest request)
    {
        if (!request.HasHotJitRequest)
        {
            return true;
        }

        if (request.HasInput)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Hot JIT disasm requires a command, not --input.[/]");
            return false;
        }

        if (request.JitInline || request.JitDisasm)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Hot JIT disasm cannot be combined with JIT dump modes.[/]");
            return false;
        }

        return true;
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
        _renderer = new ProfilerConsoleRenderer(_theme);
        _jitOutputFormatter = new JitOutputFormatter(_theme);
        _jitCommandRunner = new JitCommandRunner(_theme, _outputDirectory, _jitOutputFormatter);
        return true;
    }

    private void RunHotJitDisasm(ProfilerExecutionRequest request, CpuProfileResult results)
    {
        var rootNode = results.CallTreeRoot;
        var totalTime = results.CallTreeTotal;
        var totalSamples = rootNode.Calls;
        var title = $"JIT DISASM (HOT METHODS >= {request.RenderRequest.HotThreshold.ToString("F1", CultureInfo.InvariantCulture)})";

        if (!string.IsNullOrWhiteSpace(request.RenderRequest.CallTreeRoot))
        {
            var matches = FindCallTreeMatches(rootNode, request.RenderRequest.CallTreeRoot);
            if (matches.Count > 0)
            {
                rootNode = SelectRootMatch(matches, request.RenderRequest.IncludeRuntime, request.RenderRequest.CallTreeRootMode);
                totalTime = GetCallTreeTime(rootNode, useSelfTime: false);
                totalSamples = rootNode.Calls;
                title = $"{title} - root: {Markup.Escape(request.RenderRequest.CallTreeRoot)}";
            }
        }

        var hotMethods = CollectHotMethods(
            rootNode,
            totalTime,
            totalSamples,
            request.RenderRequest.IncludeRuntime,
            request.RenderRequest.HotThreshold);
        ConsoleThemeHelpers.PrintSection(title, _theme.AccentColor);
        if (hotMethods.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No hot methods found.[/]");
            return;
        }

        var index = 1;
        foreach (var method in hotMethods)
        {
            AnsiConsole.MarkupLine(
                $"[{_theme.AccentColor}]Disassembling ({index}/{hotMethods.Count}):[/] {Markup.Escape(method.DisplayName)}");
            var dumpFiles = _jitCommandRunner.RunDisasm(request.Command, method.Filter, suppressNoMarkersWarning: true);
            var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
            var hasMarkers = JitCommandRunner.HasDisasmMarkers(logPath ?? string.Empty);

            var fallbackFilter = BuildFallbackFilter(method.Filter);
            if (!hasMarkers && !string.Equals(fallbackFilter, method.Filter, StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine($"[{_theme.AccentColor}]Retrying with filter:[/] {Markup.Escape(fallbackFilter)}");
                dumpFiles = _jitCommandRunner.RunDisasm(request.Command, fallbackFilter, suppressNoMarkersWarning: true);
                logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
                hasMarkers = JitCommandRunner.HasDisasmMarkers(logPath ?? string.Empty);
            }

            if (!hasMarkers)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No JIT disassembly markers found. Check the method filter.[/]");
            }

            WriteOutputFiles("JIT disasm files", dumpFiles);

            if (hasMarkers && !string.IsNullOrWhiteSpace(logPath))
            {
                _jitOutputFormatter.PrintDisasmSummary(logPath);
            }

            index++;
        }
    }

    private static string BuildFallbackFilter(string filter)
    {
        var separatorIndex = filter.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < filter.Length - 1)
        {
            return filter[(separatorIndex + 1)..];
        }

        return filter;
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

internal sealed record ProfilerExecutionRequest(
    string Label,
    string Description,
    string[] Command,
    string? InputPath,
    bool HasInput,
    bool HasExplicitModes,
    bool RunCpu,
    bool RunMemory,
    bool RunHeap,
    bool RunException,
    bool RunContention,
    bool JitInline,
    bool JitDisasm,
    bool JitDisasmHot,
    bool Jit,
    bool HotThresholdSpecified,
    string? JitMethod,
    string? JitAltJitPath,
    string? JitAltJitName,
    ProfileRenderRequest RenderRequest)
{
    public bool HasHotJitRequest => JitDisasmHot || HotThresholdSpecified;

    public bool ShouldRunHotJitDisasm => HasHotJitRequest && Jit;
}
