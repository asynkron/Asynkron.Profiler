using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using static Asynkron.Profiler.CallTreeHelpers;
using Asynkron.Profiler;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;

const double HotnessFireThreshold = 0.4d;
var jitNumberRegex = MyRegex();
var versionProbeArgs = new[] { "--version" };
var theme = Theme.Current;
var renderer = new ProfilerConsoleRenderer(theme);

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
    return true;
}


IReadOnlyList<string> GetUsageExampleLines()
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

void WriteUsageExamples(TextWriter writer)
{
    foreach (var line in GetUsageExampleLines())
    {
        writer.WriteLine(line);
    }
}

int GetHelpWidth()
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


var outputDir = Path.Combine(Environment.CurrentDirectory, "profile-output");
Directory.CreateDirectory(outputDir);
var traceAnalyzer = new ProfilerTraceAnalyzer(outputDir);

var toolAvailability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
const string DotnetTraceInstall = "dotnet tool install -g dotnet-trace";
const string DotnetGcdumpInstall = "dotnet tool install -g dotnet-gcdump";

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

bool EnsureToolAvailable(string toolName, string installHint)
{
    if (toolAvailability.TryGetValue(toolName, out var cached))
    {
        return cached;
    }

    var (success, _, stderr) = ProcessRunner.Run(toolName, versionProbeArgs, timeoutMs: 10000);
    if (!success)
    {
        var detail = string.IsNullOrWhiteSpace(stderr) ? "Tool not found." : stderr.Trim();
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]{toolName} unavailable:[/] {Markup.Escape(detail)}");
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Install:[/] {Markup.Escape(installHint)}");
        toolAvailability[toolName] = false;
        return false;
    }

    toolAvailability[toolName] = true;
    return true;
}

string BuildExceptionProvider()
{
    var keywordsValue = ClrTraceEventParser.Keywords.Exception;
    var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
    return $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
}

TResult? CollectTraceAndAnalyze<TResult>(
    string[] command,
    string label,
    string traceSuffix,
    string startMessage,
    string failureLabel,
    string analysisStatus,
    Action<List<string>> configureCollectArgs,
    Func<string, TResult?> analyzeTrace)
{
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return default;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.{traceSuffix}");

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start(startMessage, ctx =>
        {
            ctx.Status("Collecting trace data...");
            var collectArgs = new List<string> { "collect" };
            configureCollectArgs(collectArgs);
            collectArgs.Add("--output");
            collectArgs.Add(traceFile);
            collectArgs.Add("--");
            collectArgs.AddRange(command);

            var (success, _, stderr) = ProcessRunner.Run("dotnet-trace", collectArgs, timeoutMs: 180000);
            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[{theme.ErrorColor}]{failureLabel}:[/] {Markup.Escape(stderr)}");
                return default;
            }

            ctx.Status(analysisStatus);
            return analyzeTrace(traceFile);
        });
}

string? CollectCpuTrace(string[] command, string label, bool includeMemory, bool includeException)
{
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.nettrace");
    var traceParts = new List<string> { "CPU" };
    if (includeMemory)
    {
        traceParts.Add("allocation");
    }

    if (includeException)
    {
        traceParts.Add("exception");
    }

    var traceLabel = string.Join(" + ", traceParts);

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Collecting {traceLabel} trace for [{theme.AccentColor}]{label}[/]...", ctx =>
        {
            ctx.Status("Collecting trace data...");
            var collectArgs = new List<string> { "collect" };
            if (includeMemory)
            {
                collectArgs.Add("--profile");
                collectArgs.Add("gc-verbose");
            }

            var providers = new List<string> { "Microsoft-DotNETCore-SampleProfiler" };
            if (includeException)
            {
                providers.Add(BuildExceptionProvider());
            }

            if (providers.Count > 0)
            {
                collectArgs.Add("--providers");
                collectArgs.Add(string.Join(",", providers));
            }

            collectArgs.Add("--output");
            collectArgs.Add(traceFile);
            collectArgs.Add("--");
            collectArgs.AddRange(command);
            var (success, _, stderr) = ProcessRunner.Run("dotnet-trace", collectArgs, timeoutMs: 180000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Trace collection failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            return traceFile;
        });
}

CpuProfileResult? CpuProfileCommand(string[] command, string label)
{
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.nettrace");

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Running CPU profile on [{theme.AccentColor}]{label}[/]...", ctx =>
        {
            ctx.Status("Collecting trace data...");
            var collectArgs = new List<string>
            {
                "collect",
                "--providers",
                "Microsoft-DotNETCore-SampleProfiler",
                "--output",
                traceFile,
                "--"
            };
            collectArgs.AddRange(command);
            var (success, _, stderr) = ProcessRunner.Run("dotnet-trace", collectArgs, timeoutMs: 180000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Trace collection failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Analyzing profile data...");
            return AnalyzeCpuTrace(traceFile);
        });
}

CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
{
    try
    {
        return ProfilerTraceAnalyzer.AnalyzeSpeedscope(speedscopePath);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Speedscope parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

CpuProfileResult? AnalyzeCpuTrace(string traceFile)
{
    try
    {
        var result = traceAnalyzer.AnalyzeCpuTrace(traceFile);
        if (result.AllFunctions.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{theme.AccentColor}]No CPU samples found in trace.[/]");
            return null;
        }

        return result;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]CPU trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
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
        var dumpFiles = JitDisasmCommand(command, method.Filter, outputDir, suppressNoMarkersWarning: true);
        var logPath = GetPrimaryJitLogPath(dumpFiles);
        var hasMarkers = HasJitDisasmMarkers(logPath ?? string.Empty);

        var fallbackFilter = method.Filter;
        var separatorIndex = fallbackFilter.LastIndexOf(':');
        if (separatorIndex >= 0 && separatorIndex < fallbackFilter.Length - 1)
        {
            fallbackFilter = fallbackFilter[(separatorIndex + 1)..];
        }

        if (!hasMarkers && !string.Equals(fallbackFilter, method.Filter, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine($"[{theme.AccentColor}]Retrying with filter:[/] {Markup.Escape(fallbackFilter)}");
            dumpFiles = JitDisasmCommand(command, fallbackFilter, outputDir, suppressNoMarkersWarning: true);
            logPath = GetPrimaryJitLogPath(dumpFiles);
            hasMarkers = HasJitDisasmMarkers(logPath ?? string.Empty);
        }

        if (!hasMarkers)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No JIT disassembly markers found. Check the method filter.[/]");
        }

        AnsiConsole.MarkupLine($"[{theme.AccentColor}]JIT disasm files:[/]");
        foreach (var file in dumpFiles)
        {
            Console.WriteLine(file);
        }

        if (hasMarkers && !string.IsNullOrWhiteSpace(logPath))
        {
            PrintJitDisasmSummary(logPath);
        }

        index++;
    }
}

CpuProfileResult? CpuProfileFromInput(string inputPath, string label)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension == ".json")
    {
        return AnalyzeSpeedscope(inputPath);
    }

    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Unsupported CPU input:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    return AnalyzeCpuTrace(inputPath);
}

MemoryProfileResult? MemoryProfileCommand(string[] command, string label)
{
    var callTree = CollectTraceAndAnalyze(
        command,
        label,
        "alloc.nettrace",
        $"Collecting allocation trace for [{theme.AccentColor}]{label}[/]...",
        "Allocation trace failed",
        "Analyzing allocation trace...",
        collectArgs =>
        {
            collectArgs.Add("--profile");
            collectArgs.Add("gc-verbose");
        },
        AnalyzeAllocationTrace);

    if (callTree == null)
    {
        return null;
    }

    return BuildMemoryProfileResult(callTree);
}

MemoryProfileResult? MemoryProfileFromInput(string inputPath, string label)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Unsupported memory input:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var callTree = AnalyzeAllocationTrace(inputPath);
    if (callTree == null)
    {
        return null;
    }

    return BuildMemoryProfileResult(callTree);
}

ExceptionProfileResult? ExceptionProfileCommand(string[] command, string label)
{
    var provider = BuildExceptionProvider();
    return CollectTraceAndAnalyze(
        command,
        label,
        "exc.nettrace",
        $"Collecting exception trace for [{theme.AccentColor}]{label}[/]...",
        "Exception trace failed",
        "Analyzing exception trace...",
        collectArgs =>
        {
            collectArgs.Add("--providers");
            collectArgs.Add(provider);
        },
        AnalyzeExceptionTrace);
}

ExceptionProfileResult? ExceptionProfileFromInput(string inputPath, string label)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Unsupported exception input:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    return AnalyzeExceptionTrace(inputPath);
}

ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
{
    try
    {
        return traceAnalyzer.AnalyzeExceptionTrace(traceFile);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Exception trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

ContentionProfileResult? ContentionProfileCommand(string[] command, string label)
{
    var keywordsValue = ClrTraceEventParser.Keywords.Contention | ClrTraceEventParser.Keywords.Threading;
    var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
    var provider = $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
    return CollectTraceAndAnalyze(
        command,
        label,
        "cont.nettrace",
        $"Collecting lock contention trace for [{theme.AccentColor}]{label}[/]...",
        "Contention trace failed",
        "Analyzing contention trace...",
        collectArgs =>
        {
            collectArgs.Add("--providers");
            collectArgs.Add(provider);
        },
        AnalyzeContentionTrace);
}

ContentionProfileResult? ContentionProfileFromInput(string inputPath, string label)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Unsupported contention input:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    return AnalyzeContentionTrace(inputPath);
}

ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
{
    try
    {
        return traceAnalyzer.AnalyzeContentionTrace(traceFile);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Contention trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
{
    try
    {
        return traceAnalyzer.AnalyzeAllocationTrace(traceFile);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Allocation trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
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

MemoryProfileResult BuildMemoryProfileResult(AllocationCallTreeResult callTree)
{
    var allocationEntries = callTree.TypeRoots
        .OrderByDescending(root => root.TotalBytes)
        .Take(50)
        .Select(root => new AllocationEntry(root.Name, root.Count, FormatBytes(root.TotalBytes)))
        .ToList();

    var totalAllocated = FormatBytes(callTree.TotalBytes);

    return new MemoryProfileResult(
        null,
        null,
        null,
        totalAllocated,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        totalAllocated,
        allocationEntries,
        callTree,
        null,
        null);
}

HeapProfileResult? HeapProfileCommand(string[] command, string label)
{
    if (!EnsureToolAvailable("dotnet-gcdump", DotnetGcdumpInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var gcdumpFile = Path.Combine(outputDir, $"{label}_{timestamp}.gcdump");

    AnsiConsole.MarkupLine("[dim]Capturing heap snapshot...[/]");

    if (command.Length == 0)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided for heap snapshot.[/]");
        return null;
    }

    var psi = new ProcessStartInfo
    {
        FileName = command[0],
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    for (var i = 1; i < command.Length; i++)
    {
        psi.ArgumentList.Add(command[i]);
    }

    using var proc = Process.Start(psi);

    if (proc == null)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Failed to start process for heap snapshot.[/]");
        return null;
    }

    Thread.Sleep(500);

    var (success, _, stderr) = ProcessRunner.Run(
        "dotnet-gcdump",
        new[]
        {
            "collect",
            "-p",
            proc.Id.ToString(CultureInfo.InvariantCulture),
            "-o",
            gcdumpFile
        },
        timeoutMs: 60000);

    proc.WaitForExit();

    if (!success || !File.Exists(gcdumpFile))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]GC dump collection failed:[/] {Markup.Escape(stderr)}");
        return null;
    }

    return GcdumpReportLoader.Load(
        gcdumpFile,
        theme,
        ProcessRunner.Run,
        ParseGcdumpReport,
        AnsiConsole.MarkupLine);
}

string[] JitInlineDumpCommand(
    string[] command,
    string jitMethod,
    string outputDir,
    string? jitAltJitPath,
    string? jitAltJitName)
{
    if (command.Length == 0)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided for JIT inline dump.[/]");
        return Array.Empty<string>();
    }

    var existing = new HashSet<string>(
        Directory.GetFiles(outputDir, "jitdump.*.txt"),
        StringComparer.OrdinalIgnoreCase);
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var stdoutFile = Path.Combine(outputDir, $"jitdump_{timestamp}.log");
    var stderrFile = Path.Combine(outputDir, $"jitdump_{timestamp}.err.log");

    AnsiConsole.MarkupLine($"[dim]Capturing JIT inlining dumps for {Markup.Escape(jitMethod)}...[/]");

    var psi = new ProcessStartInfo
    {
        FileName = command[0],
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = outputDir
    };

    for (var i = 1; i < command.Length; i++)
    {
        psi.ArgumentList.Add(command[i]);
    }

    psi.Environment["COMPlus_JitDump"] = jitMethod;
    psi.Environment["COMPlus_JitDumpInlinePhases"] = "1";
    psi.Environment["COMPlus_JitDumpASCII"] = "0";
    psi.Environment["COMPlus_TieredCompilation"] = "0";
    psi.Environment["COMPlus_ReadyToRun"] = "0";
    psi.Environment["COMPlus_ZapDisable"] = "1";
    psi.Environment["DOTNET_JitDump"] = jitMethod;
    psi.Environment["DOTNET_JitDumpInlinePhases"] = "1";
    psi.Environment["DOTNET_JitDumpASCII"] = "0";
    psi.Environment["DOTNET_TieredCompilation"] = "0";
    psi.Environment["DOTNET_ReadyToRun"] = "0";
    psi.Environment["DOTNET_ZapDisable"] = "1";
    if (!string.IsNullOrWhiteSpace(jitAltJitPath))
    {
        var altJitName = string.IsNullOrWhiteSpace(jitAltJitName) ? "clrjit" : jitAltJitName;
        psi.Environment["COMPlus_AltJit"] = jitMethod;
        psi.Environment["COMPlus_AltJitName"] = altJitName;
        psi.Environment["COMPlus_AltJitPath"] = jitAltJitPath;
        psi.Environment["DOTNET_AltJit"] = jitMethod;
        psi.Environment["DOTNET_AltJitName"] = altJitName;
        psi.Environment["DOTNET_AltJitPath"] = jitAltJitPath;
    }

    using var proc = Process.Start(psi);
    if (proc == null)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Failed to start process for JIT inline dump.[/]");
        return Array.Empty<string>();
    }

    using var stdoutWriter = new StreamWriter(stdoutFile, append: false, Encoding.UTF8);
    using var stderrWriter = new StreamWriter(stderrFile, append: false, Encoding.UTF8);
    stdoutWriter.AutoFlush = true;
    stderrWriter.AutoFlush = true;

    ProcessRunner.AttachDataHandlers(proc, stdoutWriter.WriteLine, stderrWriter.WriteLine);

    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    proc.WaitForExit();

    if (proc.ExitCode != 0)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]JIT dump process exited with code {proc.ExitCode}.[/]");
    }

    var newJitDumps = Directory.GetFiles(outputDir, "jitdump.*.txt")
        .Where(file => !existing.Contains(file))
        .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
        .Select(Path.GetFullPath);

    var results = new List<string>
    {
        Path.GetFullPath(stdoutFile)
    };

    if (new FileInfo(stderrFile).Length > 0)
    {
        results.Add(Path.GetFullPath(stderrFile));
    }

    results.AddRange(newJitDumps);

    var hasJitDumpMarkers = File.ReadLines(stdoutFile)
        .Any(line => line.Contains("JIT compiling", StringComparison.Ordinal));
    if (!hasJitDumpMarkers)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No JIT dump markers found. This usually means a Debug/Checked JIT is required.[/]");
    }

    return results.ToArray();
}

string[] JitDisasmCommand(string[] command, string jitMethod, string outputDir, bool suppressNoMarkersWarning = false)
{
    if (command.Length == 0)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided for JIT disasm.[/]");
        return Array.Empty<string>();
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var stdoutFile = Path.Combine(outputDir, $"jitdisasm_{timestamp}.log");
    var stderrFile = Path.Combine(outputDir, $"jitdisasm_{timestamp}.err.log");
    var colorFile = Path.Combine(outputDir, $"jitdisasm_{timestamp}.color.log");

    AnsiConsole.MarkupLine($"[dim]Capturing JIT disassembly for {Markup.Escape(jitMethod)}...[/]");

    var psi = new ProcessStartInfo
    {
        FileName = command[0],
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = outputDir
    };

    for (var i = 1; i < command.Length; i++)
    {
        psi.ArgumentList.Add(command[i]);
    }

    psi.Environment["COMPlus_JitDisasm"] = jitMethod;
    psi.Environment["COMPlus_TieredCompilation"] = "0";
    psi.Environment["COMPlus_ReadyToRun"] = "0";
    psi.Environment["COMPlus_ZapDisable"] = "1";
    psi.Environment["DOTNET_JitDisasm"] = jitMethod;
    psi.Environment["DOTNET_TieredCompilation"] = "0";
    psi.Environment["DOTNET_ReadyToRun"] = "0";
    psi.Environment["DOTNET_ZapDisable"] = "1";

    using var proc = Process.Start(psi);
    if (proc == null)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Failed to start process for JIT disasm.[/]");
        return Array.Empty<string>();
    }

    using var stdoutWriter = new StreamWriter(stdoutFile, append: false, Encoding.UTF8);
    using var stderrWriter = new StreamWriter(stderrFile, append: false, Encoding.UTF8);
    using var colorWriter = new StreamWriter(colorFile, append: false, Encoding.UTF8);
    stdoutWriter.AutoFlush = true;
    stderrWriter.AutoFlush = true;
    colorWriter.AutoFlush = true;

    ProcessRunner.AttachDataHandlers(
        proc,
        data =>
        {
            stdoutWriter.WriteLine(data);
            colorWriter.WriteLine(ColorizeJitDisasmLine(data));
        },
        stderrWriter.WriteLine);

    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    proc.WaitForExit();

    if (proc.ExitCode != 0)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]JIT disasm process exited with code {proc.ExitCode}.[/]");
    }

    var hasDisasmMarkers = HasJitDisasmMarkers(stdoutFile);
    if (!hasDisasmMarkers && !suppressNoMarkersWarning)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No JIT disassembly markers found. Check the method filter.[/]");
    }

    var results = new List<string>
    {
        Path.GetFullPath(stdoutFile),
        Path.GetFullPath(colorFile)
    };

    if (new FileInfo(stderrFile).Length > 0)
    {
        results.Add(Path.GetFullPath(stderrFile));
    }

    return results.ToArray();
}

bool HasJitDisasmMarkers(string logPath)
{
    if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
    {
        return false;
    }

    return File.ReadLines(logPath)
        .Any(line => line.StartsWith("; Assembly listing for method", StringComparison.Ordinal));
}

string? GetPrimaryJitLogPath(IEnumerable<string> files)
{
    foreach (var file in files)
    {
        if (file.EndsWith(".err.log", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (file.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }
    }

    return null;
}

string ColorizeJitDisasmLine(string line)
{
    if (line.Length == 0)
    {
        return line;
    }

    var commentColor = AnsiColor(theme.TreeGuideColor, dim: true);
    var labelColor = AnsiColor(theme.CpuCountColor);
    var mnemonicColor = AnsiColor(theme.TextColor);
    var numberColor = AnsiColor(theme.LeafHighlightColor);

    if (line.StartsWith(';'))
    {
        return WrapAnsi(line, commentColor);
    }

    var trimmed = line.TrimStart();
    var indent = line[..(line.Length - trimmed.Length)];
    if (trimmed.StartsWith("Runs=", StringComparison.Ordinal) ||
        trimmed.StartsWith("Done in", StringComparison.Ordinal))
    {
        return WrapAnsi(line, commentColor);
    }

    var commentIndex = trimmed.IndexOf(";;", StringComparison.Ordinal);
    var leading = commentIndex >= 0 ? trimmed[..commentIndex] : trimmed;
    var trailing = commentIndex >= 0 ? trimmed[commentIndex..] : string.Empty;

    var labelIndex = leading.IndexOf(':');
    if (labelIndex > 0 && IsLabelToken(leading[..labelIndex]))
    {
        var label = leading[..(labelIndex + 1)];
        var rest = leading[(labelIndex + 1)..];
        var restColored = ColorizeInstructionSegment(rest, mnemonicColor, numberColor);
        var highlighted = $"{WrapAnsi(label, labelColor)}{restColored}";
        if (trailing.Length > 0)
        {
            highlighted += WrapAnsi(trailing, commentColor);
        }

        return $"{indent}{highlighted}";
    }

    var instructionColored = ColorizeInstructionSegment(leading, mnemonicColor, numberColor);
    if (trailing.Length > 0)
    {
        instructionColored += WrapAnsi(trailing, commentColor);
    }

    return $"{indent}{instructionColored}";
}

string ColorizeInstructionSegment(string value, string mnemonicColor, string numberColor)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    var trimmed = value.TrimStart();
    var indent = value[..(value.Length - trimmed.Length)];
    var mnemonicEnd = 0;
    while (mnemonicEnd < trimmed.Length && !char.IsWhiteSpace(trimmed[mnemonicEnd]))
    {
        mnemonicEnd++;
    }

    if (mnemonicEnd == 0)
    {
        return value;
    }

    var mnemonic = trimmed[..mnemonicEnd];
    var rest = trimmed[mnemonicEnd..];
    var restColored = ColorizeNumbers(rest, numberColor);
    return $"{indent}{WrapAnsi(mnemonic, mnemonicColor)}{restColored}";
}

string ColorizeNumbers(string text, string color)
{
    if (string.IsNullOrEmpty(color))
    {
        return text;
    }

    return jitNumberRegex.Replace(text, match => WrapAnsi(match.Value, color));
}

bool IsLabelToken(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    foreach (var ch in value)
    {
        if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '$')
        {
            continue;
        }

        return false;
    }

    return true;
}

string AnsiColor(string hex, bool dim = false)
{
    if (!TryResolveRgb(hex, out var rgb))
    {
        return string.Empty;
    }

    var dimPrefix = dim ? "\u001b[2m" : string.Empty;
    return $"{dimPrefix}\u001b[38;2;{rgb.R};{rgb.G};{rgb.B}m";
}

bool TryResolveRgb(string value, out (byte R, byte G, byte B) rgb)
{
    if (ConsoleThemeHelpers.TryParseHexColor(value, out rgb))
    {
        return true;
    }

    var normalized = value?.Trim().ToLowerInvariant();
    rgb = normalized switch
    {
        "yellow" => (255, 255, 0),
        "red" => (255, 0, 0),
        "green" => (0, 255, 0),
        "blue" => (0, 0, 255),
        "cyan" => (0, 255, 255),
        "plum1" => (255, 187, 255),
        _ => default
    };

    return rgb != default;
}

string WrapAnsi(string text, string color)
{
    if (string.IsNullOrEmpty(color))
    {
        return text;
    }

    const string Reset = "\u001b[0m";
    return $"{color}{text}{Reset}";
}

void PrintJitDisasmSummary(string logPath)
{
    if (!File.Exists(logPath))
    {
        return;
    }

    string? methodLine = null;
    string? methodName = null;
    string? tier = null;
    string? emitting = null;
    string? pgoLine = null;
    int? inlinePgo = null;
    int? inlineSingleBlock = null;
    int? inlineNoPgo = null;
    int? blockCount = null;
    int? instructionCount = null;
    int? codeSize = null;

    var inMethod = false;
    var blocks = 0;
    var instructions = 0;

    foreach (var line in File.ReadLines(logPath))
    {
        if (line.StartsWith("; Assembly listing for method ", StringComparison.Ordinal))
        {
            if (methodLine == null)
            {
                methodLine = line.Substring("; Assembly listing for method ".Length).Trim();
                var tierStart = methodLine.LastIndexOf(" (", StringComparison.Ordinal);
                if (tierStart > 0 && methodLine.EndsWith(')'))
                {
                    tier = methodLine[(tierStart + 2)..^1];
                    methodName = methodLine[..tierStart];
                }
                else
                {
                    methodName = methodLine;
                }

                inMethod = true;
                continue;
            }

            if (inMethod)
            {
                break;
            }
        }

        if (line.StartsWith("; Emitting ", StringComparison.Ordinal))
        {
            emitting = line[2..].Trim();
            continue;
        }

        if (line.StartsWith("; No PGO data", StringComparison.Ordinal) ||
            line.Contains("inlinees", StringComparison.Ordinal))
        {
            pgoLine ??= line.TrimStart(' ', ';').Trim();
            if (line.Contains("inlinees", StringComparison.Ordinal))
            {
                var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    if (!int.TryParse(part.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var value))
                    {
                        continue;
                    }

                    if (part.Contains("inlinees with PGO data", StringComparison.Ordinal))
                    {
                        inlinePgo = value;
                    }
                    else if (part.Contains("single block inlinees", StringComparison.Ordinal))
                    {
                        inlineSingleBlock = value;
                    }
                    else if (part.Contains("inlinees without PGO data", StringComparison.Ordinal))
                    {
                        inlineNoPgo = value;
                    }
                }
            }
            continue;
        }

        if (line.Contains("code size", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(line.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var size))
            {
                codeSize ??= size;
            }
        }

        if (!inMethod)
        {
            continue;
        }

        if (line.StartsWith("G_M", StringComparison.Ordinal))
        {
            blocks++;
            continue;
        }

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == ';')
        {
            continue;
        }

        if (char.IsLetter(trimmed[0]))
        {
            instructions++;
        }
    }

    if (blocks > 0)
    {
        blockCount = blocks;
    }

    if (instructions > 0)
    {
        instructionCount = instructions;
    }

    ConsoleThemeHelpers.PrintSection("JIT DISASM SUMMARY", theme.AccentColor);
    if (string.IsNullOrWhiteSpace(methodName))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No disassembly markers found.[/]");
        return;
    }

    void PrintSummaryLine(string label, string value)
    {
        AnsiConsole.MarkupLine(
            $"[{theme.AccentColor}]{Markup.Escape(label)}[/] " +
            $"[{theme.CpuCountColor}]{Markup.Escape(value)}[/]");
    }

    PrintSummaryLine("Method:", methodName);
    if (!string.IsNullOrWhiteSpace(tier))
    {
        PrintSummaryLine("Tier:", tier);
    }

    if (!string.IsNullOrWhiteSpace(emitting))
    {
        PrintSummaryLine("Target:", emitting);
    }

    if (!string.IsNullOrWhiteSpace(pgoLine))
    {
        PrintSummaryLine("PGO:", pgoLine);
    }

    if (inlinePgo.HasValue || inlineSingleBlock.HasValue || inlineNoPgo.HasValue)
    {
        var inlineSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"PGO={inlinePgo ?? 0}, single-block={inlineSingleBlock ?? 0}, no-PGO={inlineNoPgo ?? 0}");
        PrintSummaryLine("Inlinees:", inlineSummary);
    }

    if (codeSize.HasValue)
    {
        PrintSummaryLine(
            "Code size:",
            string.Create(CultureInfo.InvariantCulture, $"{codeSize.Value} bytes"));
    }

    if (blockCount.HasValue)
    {
        PrintSummaryLine("Blocks:", blockCount.Value.ToString(CultureInfo.InvariantCulture));
    }

    if (instructionCount.HasValue)
    {
        PrintSummaryLine(
            "Instructions:",
            instructionCount.Value.ToString(CultureInfo.InvariantCulture));
    }

    if (logPath.EndsWith(".color.log", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        PrintSummaryLine("To browse the assembly, run:", $"less {logPath}");
    }
}

void PrintJitInlineSummary(string logPath)
{
    if (!File.Exists(logPath))
    {
        return;
    }

    var methodCount = 0;
    var inlineSuccess = 0;
    var inlineFailed = 0;

    foreach (var line in File.ReadLines(logPath))
    {
        if (line.StartsWith("*************** JIT compiling ", StringComparison.Ordinal))
        {
            methodCount++;
            continue;
        }

        if (line.Contains("INLINING SUCCESSFUL", StringComparison.Ordinal))
        {
            inlineSuccess++;
            continue;
        }

        if (line.Contains("INLINING FAILED", StringComparison.Ordinal))
        {
            inlineFailed++;
        }
    }

    ConsoleThemeHelpers.PrintSection("JIT INLINE SUMMARY", theme.AccentColor);
    if (methodCount == 0)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No JIT dump markers found.[/]");
        return;
    }

    AnsiConsole.MarkupLine(
        $"[{theme.AccentColor}]Methods compiled:[/] " +
        $"[{theme.CpuCountColor}]{methodCount.ToString(CultureInfo.InvariantCulture)}[/]");
    AnsiConsole.MarkupLine(
        $"[{theme.AccentColor}]Inlining:[/] " +
        $"[{theme.CpuCountColor}]" +
        $"success={inlineSuccess.ToString(CultureInfo.InvariantCulture)}, " +
        $"failed={inlineFailed.ToString(CultureInfo.InvariantCulture)}[/]");
}

HeapProfileResult? HeapProfileFromInput(string inputPath)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension == ".gcdump")
    {
        if (!EnsureToolAvailable("dotnet-gcdump", DotnetGcdumpInstall))
        {
            return null;
        }

        return GcdumpReportLoader.Load(
            inputPath,
            theme,
            ProcessRunner.Run,
            ParseGcdumpReport,
            AnsiConsole.MarkupLine);
    }

    if (extension == ".txt" || extension == ".log")
    {
        var report = File.ReadAllText(inputPath);
        return ParseGcdumpReport(report);
    }

    AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Unsupported heap input:[/] {Markup.Escape(inputPath)}");
    return null;
}

HeapProfileResult ParseGcdumpReport(string output)
{
    var types = new List<HeapTypeEntry>();
    using var reader = new StringReader(output);
    var inTable = false;

    string? line;
    while ((line = reader.ReadLine()) != null)
    {
        if (!inTable)
        {
            if (line.Contains("Size", StringComparison.Ordinal) &&
                line.Contains("Count", StringComparison.Ordinal) &&
                line.Contains("Type", StringComparison.Ordinal))
            {
                inTable = true;
            }
            continue;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            continue;
        }

        if (!TryParseLong(parts[0], out var size) || !TryParseLong(parts[1], out var count))
        {
            continue;
        }

        var typeName = string.Join(' ', parts.Skip(2));
        types.Add(new HeapTypeEntry(size, count, typeName));
    }

    return new HeapProfileResult(output, types);
}

bool TryParseLong(string input, out long value)
{
    return long.TryParse(
        input.Replace(",", "", StringComparison.Ordinal),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out value);
}

string BuildInputLabel(string inputPath)
{
    var name = Path.GetFileNameWithoutExtension(inputPath);
    if (string.IsNullOrWhiteSpace(name))
    {
        name = "input";
    }

    foreach (var invalid in Path.GetInvalidFileNameChars())
    {
        name = name.Replace(invalid, '_');
    }

    return name;
}

void ApplyInputDefaults(
    string inputPath,
    ref bool runCpu,
    ref bool runMemory,
    ref bool runHeap,
    ref bool runException,
    ref bool runContention)
{
    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    switch (extension)
    {
        case ".json":
            runCpu = true;
            break;
        case ".nettrace":
            runCpu = true;
            runException = true;
            runContention = true;
            break;
        case ".etlx":
            runMemory = true;
            runException = true;
            runContention = true;
            break;
        case ".gcdump":
        case ".txt":
        case ".log":
            runHeap = true;
            break;
        default:
            runCpu = true;
            break;
    }
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
        label = BuildInputLabel(inputPath!);
        description = inputPath!;
        if (!hasExplicitModes)
        {
            ApplyInputDefaults(inputPath!, ref runCpu, ref runMemory, ref runHeap, ref runException, ref runContention);
        }
    }
    else
    {
        if (command.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided.[/]");
            WriteUsageExamples(Console.Out);
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
            ? JitInlineDumpCommand(command, jitMethod!, outputDir, jitAltJitPath, jitAltJitName)
            : JitDisasmCommand(command, jitMethod!, outputDir);
        var labelText = jitInline ? "JIT inline dump files" : "JIT disasm files";
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]{labelText}:[/]");
        foreach (var file in dumpFiles)
        {
            Console.WriteLine(file);
        }

        var logPath = GetPrimaryJitLogPath(dumpFiles);
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            if (jitInline)
            {
                PrintJitInlineSummary(logPath);
            }
            else
            {
                PrintJitDisasmSummary(logPath);
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
        sharedTraceFile = CollectCpuTrace(command, label, runMemory, runException);
        if (sharedTraceFile == null)
        {
            return;
        }
    }

    if (runCpu && runMemory)
    {
        Console.WriteLine($"{label} - cpu+memory");
        var cpuResults = hasInput
            ? CpuProfileFromInput(inputPath!, label)
            : sharedTraceFile != null
                ? AnalyzeCpuTrace(sharedTraceFile)
                : CpuProfileCommand(command, label);
        var memoryResults = hasInput
            ? MemoryProfileFromInput(inputPath!, label)
            : sharedTraceFile != null
                ? MemoryProfileFromInput(sharedTraceFile, label)
                : MemoryProfileCommand(command, label);

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
                ? CpuProfileFromInput(inputPath!, label)
                : sharedTraceFile != null
                    ? AnalyzeCpuTrace(sharedTraceFile)
                    : CpuProfileCommand(command, label);
            RenderCpuResults(results);
        }

        if (runMemory)
        {
            Console.WriteLine($"{label} - memory");
            var results = hasInput
                ? MemoryProfileFromInput(inputPath!, label)
                : sharedTraceFile != null
                    ? MemoryProfileFromInput(sharedTraceFile, label)
                    : MemoryProfileCommand(command, label);
            RenderMemoryResults(results);
        }
    }

    if (runException)
    {
        Console.WriteLine($"{label} - exception");
        var results = hasInput
            ? ExceptionProfileFromInput(inputPath!, label)
            : sharedTraceFile != null
                ? ExceptionProfileFromInput(sharedTraceFile, label)
                : ExceptionProfileCommand(command, label);
        RenderExceptionResults(results);
    }

    if (runContention)
    {
        Console.WriteLine($"{label} - contention");
        var results = hasInput
            ? ContentionProfileFromInput(inputPath!, label)
            : ContentionProfileCommand(command, label);
        RenderContentionResults(results);
    }

    if (runHeap)
    {
        Console.WriteLine($"{label} - heap");
        var results = hasInput
            ? HeapProfileFromInput(inputPath!)
            : HeapProfileCommand(command, label);
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
    WriteUsageExamples(context.Output);
}

var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .UseHelpBuilder(_ =>
    {
        var helpBuilder = new HelpBuilder(LocalizationResources.Instance, GetHelpWidth());
        helpBuilder.CustomizeLayout(context =>
            HelpBuilder.Default.GetLayout().Concat(new HelpSectionDelegate[] { ExamplesSection }));
        return helpBuilder;
    })
    .Build();

return await parser.InvokeAsync(args);

record TableColumnSpec(string Header, bool RightAligned = false);
partial class Program
{
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
