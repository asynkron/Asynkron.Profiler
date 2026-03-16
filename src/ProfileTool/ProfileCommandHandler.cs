using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Globalization;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileCommandHandler
{
    private Theme _theme;
    private readonly string _outputDir;
    private readonly ProjectResolver _resolver;
    private ProfilerConsoleRenderer _renderer;
    private JitOutputFormatter _jitOutputFormatter;
    private JitCommandRunner _jitCommandRunner;
    private readonly ProfileTraceWorkflow _traceWorkflow;

    public ProfileCommandHandler()
    {
        _theme = Theme.Current;
        _outputDir = Path.Combine(Environment.CurrentDirectory, "profile-output");
        Directory.CreateDirectory(_outputDir);
        _resolver = new ProjectResolver(ProcessRunner.Run);
        _renderer = new ProfilerConsoleRenderer(_theme);
        _jitOutputFormatter = new JitOutputFormatter(_theme);
        _jitCommandRunner = new JitCommandRunner(_theme, _outputDir, _jitOutputFormatter);
        _traceWorkflow = new ProfileTraceWorkflow(
            _outputDir,
            () => _theme,
            () => _jitCommandRunner,
            () => _jitOutputFormatter);

        if (Console.IsOutputRedirected)
        {
            var capabilities = AnsiConsole.Profile.Capabilities;
            capabilities.Ansi = false;
            capabilities.Unicode = false;
            capabilities.Links = false;
            capabilities.Interactive = false;
            AnsiConsole.Profile.Capabilities = capabilities;
            AnsiConsole.Profile.Width = 200;
        }
    }

    public int GetHelpWidth()
    {
        if (Console.IsOutputRedirected)
        {
            return 200;
        }

        try
        {
            return Math.Max(80, Console.WindowWidth);
        }
        catch
        {
            return 120;
        }
    }

    public void WriteExamplesSection(HelpContext context, RootCommand rootCommand)
    {
        if (!ReferenceEquals(context.Command, rootCommand))
        {
            return;
        }

        context.Output.WriteLine();
        WriteUsageExamples(context.Output);
    }

    public void Handle(InvocationContext context, ProfileCommandOptions options)
    {
        var cpu = context.ParseResult.GetValueForOption(options.Cpu);
        var timeline = context.ParseResult.GetValueForOption(options.Timeline);
        var timelineWidth = context.ParseResult.GetValueForOption(options.TimelineWidth);
        var memory = context.ParseResult.GetValueForOption(options.Memory);
        var exception = context.ParseResult.GetValueForOption(options.Exception);
        var contention = context.ParseResult.GetValueForOption(options.Contention);
        var heap = context.ParseResult.GetValueForOption(options.Heap);
        var jitInline = context.ParseResult.GetValueForOption(options.JitInline);
        var jitDisasm = context.ParseResult.GetValueForOption(options.JitDisasm);
        var jitDisasmHot = context.ParseResult.GetValueForOption(options.JitDisasmHot);
        var jit = context.ParseResult.GetValueForOption(options.Jit);
        var jitMethod = context.ParseResult.GetValueForOption(options.JitMethod);
        var jitAltJitPath = context.ParseResult.GetValueForOption(options.JitAltJitPath);
        var jitAltJitName = context.ParseResult.GetValueForOption(options.JitAltJitName);
        var callTreeRoot = context.ParseResult.GetValueForOption(options.CallTreeRoot);
        var callTreeDepth = context.ParseResult.GetValueForOption(options.CallTreeDepth);
        var callTreeWidth = context.ParseResult.GetValueForOption(options.CallTreeWidth);
        var callTreeRootMode = context.ParseResult.GetValueForOption(options.CallTreeRootMode);
        var callTreeSelf = context.ParseResult.GetValueForOption(options.CallTreeSelf);
        var callTreeSiblingCutoff = context.ParseResult.GetValueForOption(options.CallTreeSiblingCutoff);
        var hotThresholdInput = context.ParseResult.GetValueForOption(options.HotThreshold);
        var hotThresholdSpecified = context.ParseResult.FindResultFor(options.HotThreshold) != null;
        if (!ProfileTraceWorkflow.TryParseHotThreshold(hotThresholdInput, out var hotThreshold))
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]--hot must be a number between 0 and 1 (use 0.3 or 0,3).[/]");
            return;
        }

        var functionFilter = context.ParseResult.GetValueForOption(options.FunctionFilter);
        var exceptionTypeFilter = context.ParseResult.GetValueForOption(options.ExceptionType);
        var includeRuntime = context.ParseResult.GetValueForOption(options.IncludeRuntime);
        var inputPath = context.ParseResult.GetValueForOption(options.Input);
        var targetFramework = context.ParseResult.GetValueForOption(options.TargetFramework);
        var themeName = context.ParseResult.GetValueForOption(options.Theme);
        var command = context.ParseResult.GetValueForArgument(options.Command) ?? Array.Empty<string>();

        if (!TryApplyTheme(themeName))
        {
            return;
        }

        var inputLoader = _traceWorkflow.InputLoader;
        var hasInput = !string.IsNullOrWhiteSpace(inputPath);
        var hasExplicitModes = cpu || memory || heap || contention || exception;
        var runCpu = cpu || !hasExplicitModes;
        var runMemory = memory || !hasExplicitModes;
        var runHeap = heap;
        var runException = exception;
        var runContention = contention;

        if (jitDisasmHot || hotThresholdSpecified)
        {
            runCpu = true;
        }

        string label;
        string description;
        if (hasInput)
        {
            label = ProfileInputLoader.BuildInputLabel(inputPath!);
            description = inputPath!;
            if (!hasExplicitModes)
            {
                ProfileInputLoader.ApplyInputDefaults(
                    inputPath!,
                    ref runCpu,
                    ref runMemory,
                    ref runHeap,
                    ref runException,
                    ref runContention);
            }
        }
        else
        {
            if (command.Length == 0)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No command provided.[/]");
                WriteUsageExamples(Console.Out);
                return;
            }

            var resolved = _resolver.Resolve(command, targetFramework);
            if (resolved == null)
            {
                return;
            }

            command = resolved.Command;
            label = resolved.Label;
            description = resolved.Description;
        }

        var renderRequest = new ProfileRenderRequest(
            label,
            description,
            callTreeRoot,
            functionFilter,
            exceptionTypeFilter,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSelf,
            callTreeSiblingCutoff,
            hotThreshold,
            timeline,
            timelineWidth);

        void RenderCpuResults(CpuProfileResult? results, MemoryProfileResult? memoryResults = null)
        {
            _renderer.PrintCpuResults(results, renderRequest, memoryResults);

            if ((jitDisasmHot || hotThresholdSpecified) && jit && results != null)
            {
                _traceWorkflow.RunHotJitDisasm(
                    results,
                    command,
                    renderRequest.CallTreeRoot,
                    renderRequest.CallTreeRootMode,
                    renderRequest.IncludeRuntime,
                    renderRequest.HotThreshold);
            }
        }

        void RenderMemoryResults(MemoryProfileResult? results) => _renderer.PrintMemoryResults(results, renderRequest);
        void RenderExceptionResults(ExceptionProfileResult? results) => _renderer.PrintExceptionResults(results, renderRequest);
        void RenderContentionResults(ContentionProfileResult? results) => _renderer.PrintContentionResults(results, renderRequest);
        void RenderHeapResults(HeapProfileResult? results) => _renderer.PrintHeapResults(results, renderRequest);

        if (jitInline || jitDisasm)
        {
            if (hasInput)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]JIT dump modes require a command, not --input.[/]");
                return;
            }

            if (hasExplicitModes)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]JIT dump modes cannot be combined with other profiling modes.[/]");
                return;
            }

            if (string.IsNullOrWhiteSpace(jitMethod))
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Missing --jit-method (e.g. Namespace.Type:Method).[/]");
                return;
            }

            if (jitInline && jitDisasm)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Choose either --jit-inline or --jit-disasm, not both.[/]");
                return;
            }

            if (!string.IsNullOrWhiteSpace(jitAltJitPath) && !File.Exists(jitAltJitPath))
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]AltJit path not found:[/] {Markup.Escape(jitAltJitPath)}");
                return;
            }

            var dumpFiles = jitInline
                ? _jitCommandRunner.RunInlineDump(command, jitMethod!, jitAltJitPath, jitAltJitName)
                : _jitCommandRunner.RunDisasm(command, jitMethod!);
            PrintDumpFiles(jitInline ? "JIT inline dump files" : "JIT disasm files", dumpFiles);

            var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                if (jitInline)
                {
                    _jitOutputFormatter.PrintInlineSummary(logPath);
                }
                else
                {
                    _jitOutputFormatter.PrintDisasmSummary(logPath);
                }
            }

            return;
        }

        if (jitDisasmHot || hotThresholdSpecified)
        {
            if (hasInput)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Hot JIT disasm requires a command, not --input.[/]");
                return;
            }

            if (jitInline || jitDisasm)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Hot JIT disasm cannot be combined with JIT dump modes.[/]");
                return;
            }
        }

        string? sharedTraceFile = null;
        if (!hasInput && runCpu && (runMemory || runException))
        {
            sharedTraceFile = _traceWorkflow.CollectCpuTrace(command, label, runMemory, runException);
            if (sharedTraceFile == null)
            {
                return;
            }
        }

        if (runCpu && runMemory)
        {
            Console.WriteLine($"{label} - cpu+memory");
            var cpuResults = hasInput
                ? inputLoader.LoadCpu(inputPath!)
                : sharedTraceFile != null
                    ? inputLoader.AnalyzeCpuTrace(sharedTraceFile)
                    : _traceWorkflow.ProfileCpu(command, label);
            var memoryResults = hasInput
                ? inputLoader.LoadMemory(inputPath!)
                : sharedTraceFile != null
                    ? inputLoader.LoadMemory(sharedTraceFile)
                    : _traceWorkflow.ProfileMemory(command, label);

            if (cpuResults != null)
            {
                RenderCpuResults(cpuResults, memoryResults);
            }
            else if (memoryResults != null)
            {
                RenderMemoryResults(memoryResults);
            }
        }
        else
        {
            if (runCpu)
            {
                Console.WriteLine($"{label} - cpu");
                var results = hasInput
                    ? inputLoader.LoadCpu(inputPath!)
                    : sharedTraceFile != null
                        ? inputLoader.AnalyzeCpuTrace(sharedTraceFile)
                        : _traceWorkflow.ProfileCpu(command, label);
                RenderCpuResults(results);
            }

            if (runMemory)
            {
                Console.WriteLine($"{label} - memory");
                var results = hasInput
                    ? inputLoader.LoadMemory(inputPath!)
                    : sharedTraceFile != null
                        ? inputLoader.LoadMemory(sharedTraceFile)
                        : _traceWorkflow.ProfileMemory(command, label);
                RenderMemoryResults(results);
            }
        }

        if (runException)
        {
            Console.WriteLine($"{label} - exception");
            var results = hasInput
                ? inputLoader.LoadException(inputPath!)
                : sharedTraceFile != null
                    ? inputLoader.LoadException(sharedTraceFile)
                    : _traceWorkflow.ProfileException(command, label);
            RenderExceptionResults(results);
        }

        if (runContention)
        {
            Console.WriteLine($"{label} - contention");
            var results = hasInput
                ? inputLoader.LoadContention(inputPath!)
                : _traceWorkflow.ProfileContention(command, label);
            RenderContentionResults(results);
        }

        if (runHeap)
        {
            Console.WriteLine($"{label} - heap");
            var results = hasInput
                ? inputLoader.LoadHeap(inputPath!)
                : _traceWorkflow.ProfileHeap(command, label);
            RenderHeapResults(results);
        }
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
        _jitCommandRunner = new JitCommandRunner(_theme, _outputDir, _jitOutputFormatter);
        return true;
    }

    private static void PrintDumpFiles(string labelText, IEnumerable<string> dumpFiles)
    {
        ProfileFileListWriter.Write(labelText, Theme.Current.AccentColor, dumpFiles);
    }

    private static IEnumerable<string> GetUsageExampleLines()
    {
        return new[]
        {
            "Examples:",
            "",
            "CPU profiling:",
            "  asynkron-profiler --cpu -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --cpu --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --cpu --calltree-depth 5 -- ./MyApp.csproj",
            "  asynkron-profiler --cpu --calltree-depth 5 -- ./MySolution.sln",
            "  asynkron-profiler --cpu --input ./profile-output/app.speedscope.json",
            "  asynkron-profiler --cpu --timeline -- ./bin/Release/<tfm>/MyApp",
            "",
            "Memory profiling:",
            "  asynkron-profiler --memory -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --memory --root \"MyNamespace\" -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --memory --input ./profile-output/app.nettrace",
            "",
            "Exception profiling:",
            "  asynkron-profiler --exception -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --exception --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --exception --exception-type \"InvalidOperation\" -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --exception --input ./profile-output/app.nettrace",
            "",
            "Lock contention profiling:",
            "  asynkron-profiler --contention -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --contention --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --contention --input ./profile-output/app.nettrace",
            "",
            "Heap snapshot:",
            "  asynkron-profiler --heap -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --heap --input ./profile-output/app.gcdump",
            "",
            "JIT inlining dumps:",
            "  asynkron-profiler --jit-inline --jit-method \"Namespace.Type:Method\" -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --jit-inline --jit-method \"Namespace.Type:Method\" --jit-altjit-path /path/to/libclrjit.dylib -- ./bin/Release/<tfm>/MyApp",
            "JIT disassembly:",
            "  asynkron-profiler --jit-disasm --jit-method \"Namespace.Type:Method\" -- ./bin/Release/<tfm>/MyApp",
            "",
            "Render existing traces:",
            "  asynkron-profiler --input ./profile-output/app.nettrace",
            "  asynkron-profiler --input ./profile-output/app.speedscope.json --cpu",
            "  asynkron-profiler --input ./profile-output/app.etlx --memory",
            "  asynkron-profiler --input ./profile-output/app.nettrace --exception",
            "",
            "General:",
            "  asynkron-profiler --help",
            "",
            "Themes:",
            "  asynkron-profiler --theme onedark --cpu -- ./bin/Release/<tfm>/MyApp",
            $"  Available: {Theme.AvailableThemes}"
        };
    }

    private static void WriteUsageExamples(TextWriter writer)
    {
        foreach (var line in GetUsageExampleLines())
        {
            writer.WriteLine(line);
        }
    }
}
