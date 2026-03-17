using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Linq;
using Asynkron.Profiler;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

const double HotnessFireThreshold = 0.4d;
var theme = Theme.Current;
var outputDir = Path.Combine(Environment.CurrentDirectory, "profile-output");
Directory.CreateDirectory(outputDir);
var renderer = new ProfilerConsoleRenderer(theme);
var jitOutputFormatter = new JitOutputFormatter(theme);
var jitCommandRunner = new JitCommandRunner(theme, outputDir, jitOutputFormatter);
var toolAvailability = new ProfilerToolAvailability(() => theme, ProcessRunner.Run, AnsiConsole.MarkupLine);
var traceAnalyzer = new ProfilerTraceAnalyzer(outputDir);
var profileInputLoader = new ProfileInputLoader(
    traceAnalyzer,
    () => theme,
    toolAvailability.EnsureAvailable,
    ProcessRunner.Run,
    GcdumpReportParser.Parse,
    AnsiConsole.MarkupLine,
    ProfileCollectionRunner.DotnetGcdumpInstallHint);
var collectionRunner = new ProfileCollectionRunner(
    outputDir,
    () => theme,
    toolAvailability.EnsureAvailable,
    ProcessRunner.Run,
    profileInputLoader,
    AnsiConsole.MarkupLine);

bool TryApplyTheme(string? themeName)
{
    if (!Theme.TryResolve(themeName, out var selectedTheme))
    {
        var name = themeName ?? string.Empty;
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Unknown theme '{Markup.Escape(name)}'[/]");
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Available themes:[/] {Theme.AvailableThemes}");
        return false;
    }

    Theme.Current = selectedTheme;
    theme = selectedTheme;
    renderer = new ProfilerConsoleRenderer(theme);
    jitOutputFormatter = new JitOutputFormatter(theme);
    jitCommandRunner = new JitCommandRunner(theme, outputDir, jitOutputFormatter);
    return true;
}

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

void RunHotJitDisasm(
    CpuProfileResult results,
    string[] command,
    string? rootFilter,
    string? rootMode,
    bool includeRuntime,
    double hotThreshold)
{
    var rootNode = results.CallTreeRoot;
    var totalTime = results.CallTreeTotal;
    var totalSamples = rootNode.Calls;
    var title = $"JIT DISASM (HOT METHODS >= {hotThreshold.ToString("F1", CultureInfo.InvariantCulture)})";

    if (!string.IsNullOrWhiteSpace(rootFilter))
    {
        var matches = FindCallTreeMatches(rootNode, rootFilter);
        if (matches.Count > 0)
        {
            rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
            totalTime = GetCallTreeTime(rootNode, useSelfTime: false);
            totalSamples = rootNode.Calls;
            title = $"{title} - root: {Markup.Escape(rootFilter)}";
        }
    }

    var hotMethods = CollectHotMethods(rootNode, totalTime, totalSamples, includeRuntime, hotThreshold);
    ConsoleThemeHelpers.PrintSection(title, theme.AccentColor);
    if (hotMethods.Count == 0)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]No hot methods found.[/]");
        return;
    }

    var index = 1;
    foreach (var method in hotMethods)
    {
        AnsiConsole.MarkupLine(
            $"[{theme.AccentColor}]Disassembling ({index}/{hotMethods.Count}):[/] {Markup.Escape(method.DisplayName)}");
        var dumpFiles = jitCommandRunner.RunDisasm(command, method.Filter, suppressNoMarkersWarning: true);
        var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
        var hasMarkers = JitCommandRunner.HasDisasmMarkers(logPath ?? string.Empty);

        var fallbackFilter = method.Filter;
        var separatorIndex = fallbackFilter.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < fallbackFilter.Length - 1)
        {
            fallbackFilter = fallbackFilter[(separatorIndex + 1)..];
        }

        if (!hasMarkers && !string.Equals(fallbackFilter, method.Filter, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"[{theme.AccentColor}]Retrying with filter:[/] {Markup.Escape(fallbackFilter)}");
            dumpFiles = jitCommandRunner.RunDisasm(command, fallbackFilter, suppressNoMarkersWarning: true);
            logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
            hasMarkers = JitCommandRunner.HasDisasmMarkers(logPath ?? string.Empty);
        }

        if (!hasMarkers)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No JIT disassembly markers found. Check the method filter.[/]");
        }

        WriteOutputFiles("JIT disasm files", dumpFiles);

        if (hasMarkers && !string.IsNullOrWhiteSpace(logPath))
        {
            jitOutputFormatter.PrintDisasmSummary(logPath);
        }

        index++;
    }
}

void WriteOutputFiles(string label, IEnumerable<string> files)
{
    AnsiConsole.MarkupLine($"[{theme.AccentColor}]{label}:[/]");
    foreach (var file in files)
    {
        Console.WriteLine(file);
    }
}

bool TryParseHotThreshold(string? input, out double value)
{
    value = HotnessFireThreshold;
    if (string.IsNullOrWhiteSpace(input))
    {
        return true;
    }

    var trimmed = input.Trim();
    if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
    {
        return value is >= 0d and <= 1d;
    }

    if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
    {
        return value is >= 0d and <= 1d;
    }

    return false;
}

// Command-line setup
var cpuOption = new Option<bool>("--cpu", "Run CPU profiling only");
var timelineOption = new Option<bool>("--timeline", "Show inline timeline bars in call tree (use with --cpu)");
var timelineWidthOption = new Option<int>("--timeline-width", () => 40, "Timeline bar width in characters (default: 40)");
var memoryOption = new Option<bool>("--memory", "Run memory profiling only");
var exceptionOption = new Option<bool>("--exception", "Run exception profiling only");
exceptionOption.AddAlias("--exceptions");
var contentionOption = new Option<bool>("--contention", "Run lock contention profiling only");
var heapOption = new Option<bool>("--heap", "Capture heap snapshot");
var jitInlineOption = new Option<bool>("--jit-inline", "Capture JIT inlining dumps to files (no parsing)");
var jitDisasmOption = new Option<bool>("--jit-disasm", "Capture JIT disassembly output to files (no parsing)");
var jitDisasmHotOption = new Option<bool>("--jit-disasm-hot", "Capture JIT disassembly for hot methods after CPU profiling");
var jitOption = new Option<bool>("--jit", "Enable JIT decompilation for hot methods (requires --hot)");
var jitMethodOption = new Option<string?>("--jit-method", "Method filter for JIT dumps (e.g. Namespace.Type:Method)");
var jitAltJitPathOption = new Option<string?>("--jit-altjit-path", "Path to a Debug/Checked JIT (libclrjit) for JitDump");
var jitAltJitNameOption = new Option<string?>("--jit-altjit-name", () => "clrjit", "AltJit name (default: clrjit)");
var callTreeRootOption = new Option<string?>("--root", "Filter call tree to a root method (substring match)");
var callTreeDepthOption = new Option<int>("--calltree-depth", () => 30, "Maximum call tree depth (default: 30)");
var callTreeWidthOption = new Option<int>("--calltree-width", () => 4, "Maximum children per node (default: 4)");
var callTreeRootModeOption = new Option<string?>("--root-mode", () => "hottest", "Root selection mode when multiple matches (hottest|shallowest|first)");
var callTreeSelfOption = new Option<bool>("--calltree-self", "Show self-time call tree in addition to total time");
var callTreeSiblingCutoffOption = new Option<int>("--calltree-sibling-cutoff", () => 5, "Hide siblings below X% of the top sibling (default: 5)");
var hotThresholdOption = new Option<string?>(
    "--hot",
    () => HotnessFireThreshold.ToString(CultureInfo.InvariantCulture),
    "Hotness threshold for hotspot markers/JIT disasm (0-1)");
var functionFilterOption = new Option<string?>("--filter", "Filter CPU function tables by substring (case-insensitive)");
var exceptionTypeOption = new Option<string?>("--exception-type", "Filter exception tables and call trees by exception type (substring match)");
var includeRuntimeOption = new Option<bool>("--include-runtime", "Include runtime/process frames in CPU tables and call tree");
var inputOption = new Option<string?>("--input", "Render results from an existing trace file");
var targetFrameworkOption = new Option<string?>("--tfm", "Target framework to use for .csproj/.sln inputs (e.g. net8.0)");
var themeOption = new Option<string?>("--theme", "Color theme (default|onedark|dracula|nord|monokai)");
themeOption.AddAlias("-t");
var commandArg = new Argument<string[]>("command", () => Array.Empty<string>(),
    "Command to profile (pass after --)");
commandArg.Arity = ArgumentArity.ZeroOrMore;

var rootCommand = new RootCommand("Asynkron Profiler - CPU/Memory/Exception/Contention/Heap profiling for .NET commands")
{
    cpuOption,
    timelineOption,
    timelineWidthOption,
    memoryOption,
    exceptionOption,
    contentionOption,
    heapOption,
    jitInlineOption,
    jitDisasmOption,
    jitDisasmHotOption,
    jitOption,
    jitMethodOption,
    jitAltJitPathOption,
    jitAltJitNameOption,
    callTreeRootOption,
    callTreeDepthOption,
    callTreeWidthOption,
    callTreeRootModeOption,
    callTreeSelfOption,
    callTreeSiblingCutoffOption,
    hotThresholdOption,
    functionFilterOption,
    exceptionTypeOption,
    includeRuntimeOption,
    inputOption,
    targetFrameworkOption,
    themeOption,
    commandArg
};

rootCommand.TreatUnmatchedTokensAsErrors = false;

rootCommand.SetHandler(context =>
{
    var cpu = context.ParseResult.GetValueForOption(cpuOption);
    var timeline = context.ParseResult.GetValueForOption(timelineOption);
    var timelineWidth = context.ParseResult.GetValueForOption(timelineWidthOption);
    var memory = context.ParseResult.GetValueForOption(memoryOption);
    var exception = context.ParseResult.GetValueForOption(exceptionOption);
    var contention = context.ParseResult.GetValueForOption(contentionOption);
    var heap = context.ParseResult.GetValueForOption(heapOption);
    var jitInline = context.ParseResult.GetValueForOption(jitInlineOption);
    var jitDisasm = context.ParseResult.GetValueForOption(jitDisasmOption);
    var jitDisasmHot = context.ParseResult.GetValueForOption(jitDisasmHotOption);
    var jit = context.ParseResult.GetValueForOption(jitOption);
    var jitMethod = context.ParseResult.GetValueForOption(jitMethodOption);
    var jitAltJitPath = context.ParseResult.GetValueForOption(jitAltJitPathOption);
    var jitAltJitName = context.ParseResult.GetValueForOption(jitAltJitNameOption);
    var callTreeRoot = context.ParseResult.GetValueForOption(callTreeRootOption);
    var callTreeDepth = context.ParseResult.GetValueForOption(callTreeDepthOption);
    var callTreeWidth = context.ParseResult.GetValueForOption(callTreeWidthOption);
    var callTreeRootMode = context.ParseResult.GetValueForOption(callTreeRootModeOption);
    var callTreeSelf = context.ParseResult.GetValueForOption(callTreeSelfOption);
    var callTreeSiblingCutoff = context.ParseResult.GetValueForOption(callTreeSiblingCutoffOption);
    var hotThresholdInput = context.ParseResult.GetValueForOption(hotThresholdOption);
    var hotThresholdSpecified = context.ParseResult.FindResultFor(hotThresholdOption) != null;
    if (!TryParseHotThreshold(hotThresholdInput, out var hotThreshold))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]--hot must be a number between 0 and 1 (use 0.3 or 0,3).[/]");
        return;
    }
    var functionFilter = context.ParseResult.GetValueForOption(functionFilterOption);
    var exceptionTypeFilter = context.ParseResult.GetValueForOption(exceptionTypeOption);
    var includeRuntime = context.ParseResult.GetValueForOption(includeRuntimeOption);
    var inputPath = context.ParseResult.GetValueForOption(inputOption);
    var targetFramework = context.ParseResult.GetValueForOption(targetFrameworkOption);
    var themeName = context.ParseResult.GetValueForOption(themeOption);
    var command = context.ParseResult.GetValueForArgument(commandArg) ?? Array.Empty<string>();

    if (!TryApplyTheme(themeName))
    {
        return;
    }

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


    var resolver = new ProjectResolver(ProcessRunner.Run);
    string label;
    string description;
    if (hasInput)
    {
        label = ProfileInputLoader.BuildInputLabel(inputPath!);
        description = inputPath!;
        if (!hasExplicitModes)
        {
            ProfileInputLoader.ApplyInputDefaults(inputPath!, ref runCpu, ref runMemory, ref runHeap, ref runException, ref runContention);
        }
    }
    else
    {
        if (command.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided.[/]");
            ProfilerCommandLineHelp.WriteUsageExamples(Console.Out);
            return;
        }

        var resolved = resolver.Resolve(command, targetFramework);
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
        renderer.PrintCpuResults(results, renderRequest, memoryResults);

        if ((jitDisasmHot || hotThresholdSpecified) && jit && results != null)
        {
            RunHotJitDisasm(
                results,
                command,
                renderRequest.CallTreeRoot,
                renderRequest.CallTreeRootMode,
                renderRequest.IncludeRuntime,
                renderRequest.HotThreshold);
        }
    }

    void RenderMemoryResults(MemoryProfileResult? results) => renderer.PrintMemoryResults(results, renderRequest);

    void RenderExceptionResults(ExceptionProfileResult? results) => renderer.PrintExceptionResults(results, renderRequest);

    void RenderContentionResults(ContentionProfileResult? results) => renderer.PrintContentionResults(results, renderRequest);

    void RenderHeapResults(HeapProfileResult? results) => renderer.PrintHeapResults(results, renderRequest);

    if (jitInline || jitDisasm)
    {
        if (hasInput)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]JIT dump modes require a command, not --input.[/]");
            return;
        }

        if (hasExplicitModes)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]JIT dump modes cannot be combined with other profiling modes.[/]");
            return;
        }

        if (string.IsNullOrWhiteSpace(jitMethod))
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Missing --jit-method (e.g. Namespace.Type:Method).[/]");
            return;
        }

        if (jitInline && jitDisasm)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Choose either --jit-inline or --jit-disasm, not both.[/]");
            return;
        }

        if (!string.IsNullOrWhiteSpace(jitAltJitPath) && !File.Exists(jitAltJitPath))
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]AltJit path not found:[/] {Markup.Escape(jitAltJitPath)}");
            return;
        }

        var dumpFiles = jitInline
            ? jitCommandRunner.RunInlineDump(command, jitMethod!, jitAltJitPath, jitAltJitName)
            : jitCommandRunner.RunDisasm(command, jitMethod!);
        WriteOutputFiles(jitInline ? "JIT inline dump files" : "JIT disasm files", dumpFiles);

        var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            if (jitInline)
            {
                jitOutputFormatter.PrintInlineSummary(logPath);
            }
            else
            {
                jitOutputFormatter.PrintDisasmSummary(logPath);
            }
        }

        return;
    }

    if (jitDisasmHot || hotThresholdSpecified)
    {
        if (hasInput)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Hot JIT disasm requires a command, not --input.[/]");
            return;
        }

        if (jitInline || jitDisasm)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Hot JIT disasm cannot be combined with JIT dump modes.[/]");
            return;
        }
    }

    string? sharedTraceFile = null;
    if (!hasInput && runCpu && (runMemory || runException))
    {
        sharedTraceFile = collectionRunner.CollectCpuTrace(command, label, runMemory, runException);
        if (sharedTraceFile == null)
        {
            return;
        }
    }

    if (runCpu && runMemory)
    {
        Console.WriteLine($"{label} - cpu+memory");
        var cpuResults = hasInput
            ? profileInputLoader.LoadCpu(inputPath!)
            : sharedTraceFile != null
                ? profileInputLoader.AnalyzeCpuTrace(sharedTraceFile)
                : collectionRunner.RunCpuProfile(command, label);
        var memoryResults = hasInput
            ? profileInputLoader.LoadMemory(inputPath!)
            : sharedTraceFile != null
                ? profileInputLoader.LoadMemory(sharedTraceFile)
                : collectionRunner.RunMemoryProfile(command, label);

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
                ? profileInputLoader.LoadCpu(inputPath!)
                : sharedTraceFile != null
                    ? profileInputLoader.AnalyzeCpuTrace(sharedTraceFile)
                    : collectionRunner.RunCpuProfile(command, label);
            RenderCpuResults(results);
        }

        if (runMemory)
        {
            Console.WriteLine($"{label} - memory");
            var results = hasInput
                ? profileInputLoader.LoadMemory(inputPath!)
                : sharedTraceFile != null
                    ? profileInputLoader.LoadMemory(sharedTraceFile)
                    : collectionRunner.RunMemoryProfile(command, label);
            RenderMemoryResults(results);
        }
    }

    if (runException)
    {
        Console.WriteLine($"{label} - exception");
        var results = hasInput
            ? profileInputLoader.LoadException(inputPath!)
            : sharedTraceFile != null
                ? profileInputLoader.LoadException(sharedTraceFile)
                : collectionRunner.RunExceptionProfile(command, label);
        RenderExceptionResults(results);
    }

    if (runContention)
    {
        Console.WriteLine($"{label} - contention");
        var results = hasInput
            ? profileInputLoader.LoadContention(inputPath!)
            : collectionRunner.RunContentionProfile(command, label);
        RenderContentionResults(results);
    }

    if (runHeap)
    {
        Console.WriteLine($"{label} - heap");
        var results = hasInput
            ? profileInputLoader.LoadHeap(inputPath!)
            : collectionRunner.RunHeapProfile(command, label);
        RenderHeapResults(results);
    }
});

void ExamplesSection(HelpContext context)
{
    if (!ReferenceEquals(context.Command, rootCommand))
    {
        return;
    }

    context.Output.WriteLine();
    ProfilerCommandLineHelp.WriteUsageExamples(context.Output);
}

var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseHelpBuilder(_ =>
    {
        var helpBuilder = new HelpBuilder(LocalizationResources.Instance, ProfilerCommandLineHelp.GetHelpWidth());
        helpBuilder.CustomizeLayout(context =>
            HelpBuilder.Default.GetLayout().Concat(new HelpSectionDelegate[] { ExamplesSection }));
        return helpBuilder;
    })
    .Build();

return await parser.InvokeAsync(args);

record TableColumnSpec(string Header, bool RightAligned = false);
