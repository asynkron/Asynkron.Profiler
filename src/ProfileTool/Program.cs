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
using Asynkron.Profiler;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;
using Spectre.Console.Rendering;

const double HotnessFireThreshold = 0.4d;
var jitNumberRegex = new Regex(
    @"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])",
    RegexOptions.Compiled);
var theme = Theme.Current;
var treeGuideStyle = new Style(ParseColor(theme.TreeGuideColor));
var renderer = new ProfilerConsoleRenderer(theme);

void PrintSection(string text, string? color = null)
{
    Console.WriteLine();
    if (string.IsNullOrWhiteSpace(color))
    {
        Console.WriteLine(text);
        return;
    }

    AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
}

Color ParseColor(string hex)
{
    if (string.IsNullOrWhiteSpace(hex))
    {
        return Color.Default;
    }

    var value = Convert.ToInt32(hex.TrimStart('#'), 16);
    return new Color(
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)(value & 0xFF));
}

bool TryParseHexColor(string value, out (byte R, byte G, byte B) rgb)
{
    rgb = default;
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var trimmed = value.Trim();
    if (trimmed.StartsWith("#", StringComparison.Ordinal))
    {
        trimmed = trimmed[1..];
    }

    if (trimmed.Length != 6)
    {
        return false;
    }

    if (!byte.TryParse(trimmed.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
        !byte.TryParse(trimmed.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
        !byte.TryParse(trimmed.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
    {
        return false;
    }

    rgb = (r, g, b);
    return true;
}

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
    treeGuideStyle = new Style(ParseColor(theme.TreeGuideColor));
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

(bool Success, string StdOut, string StdErr) RunProcess(
    string fileName,
    IEnumerable<string> args,
    string? workingDir = null,
    int timeoutMs = 300000)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            psi.WorkingDirectory = workingDir;
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            return (false, string.Empty, "Failed to start process.");
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (stdoutLock)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (stderrLock)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures.
            }

            return (false, stdout.ToString(), $"Process timed out after {timeoutMs} ms.");
        }

        process.WaitForExit();
        return (process.ExitCode == 0, stdout.ToString(), stderr.ToString());
    }
    catch (Exception ex)
    {
        return (false, string.Empty, ex.Message);
    }
}

var outputDir = Path.Combine(Environment.CurrentDirectory, "profile-output");
Directory.CreateDirectory(outputDir);

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

    var (success, _, stderr) = RunProcess(toolName, new[] { "--version" }, timeoutMs: 10000);
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
            var (success, _, stderr) = RunProcess("dotnet-trace", collectArgs, timeoutMs: 180000);

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
            var (success, _, stderr) = RunProcess("dotnet-trace", collectArgs, timeoutMs: 180000);

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
    return SpeedscopeParser.ParseFile(speedscopePath);
}

CpuProfileResult? AnalyzeCpuTrace(string traceFile)
{
    try
    {
        var frameTotals = new Dictionary<string, double>(StringComparer.Ordinal);
        var frameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var frameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var framesList = new List<string>();
        var callTreeRoot = new CallTreeNode(-1, "Total");
        var callTreeTotal = 0d;
        var totalSamples = 0L;
        double? lastSampleTimeMs = null;
        var sawTypedException = false;

        var etlxPath = traceFile;
        if (traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(traceFile);
            var targetPath = Path.Combine(outputDir, $"{fileName}.etlx");
            var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
            etlxPath = TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
        }

        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();

        const string sampleProfilerProvider = "Microsoft-DotNETCore-SampleProfiler";

        void RecordException(TraceEvent data)
        {
            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var typeName = GetExceptionTypeName(data);
            var frames = EnumerateCpuFrames(stack).ToList();
            if (frames.Count == 0)
            {
                frames.Add("Unknown");
            }

            frames.Reverse();

            var node = callTreeRoot;
            node.AddExceptionTotals(1);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (!frameIndices.TryGetValue(frame, out var frameIdx))
                {
                    frameIdx = framesList.Count;
                    framesList.Add(frame);
                    frameIndices[frame] = frameIdx;
                }

                if (!node.Children.TryGetValue(frameIdx, out var child))
                {
                    child = new CallTreeNode(frameIdx, frame);
                    node.Children[frameIdx] = child;
                }

                if (i == frames.Count - 1)
                {
                    child.AddException(typeName, 1);
                }
                else
                {
                    child.AddExceptionTotals(1);
                }

                node = child;
            }
        }

        source.Clr.GCAllocationTick += data =>
        {
            var bytes = data.AllocationAmount64;
            if (bytes <= 0)
            {
                return;
            }

            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var typeName = string.IsNullOrWhiteSpace(data.TypeName) ? "Unknown" : data.TypeName;
            var frames = EnumerateCpuFrames(stack).ToList();
            if (frames.Count == 0)
            {
                frames.Add("Unknown");
            }

            frames.Reverse();

            var node = callTreeRoot;
            node.AddAllocationTotals(bytes);
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (!frameIndices.TryGetValue(frame, out var frameIdx))
                {
                    frameIdx = framesList.Count;
                    framesList.Add(frame);
                    frameIndices[frame] = frameIdx;
                }

                if (!node.Children.TryGetValue(frameIdx, out var child))
                {
                    child = new CallTreeNode(frameIdx, frame);
                    node.Children[frameIdx] = child;
                }

                if (i == frames.Count - 1)
                {
                    child.AddAllocation(typeName, bytes);
                }
                else
                {
                    child.AddAllocationTotals(bytes);
                }

                node = child;
            }
        };

        source.Clr.ExceptionStart += data =>
        {
            sawTypedException = true;
            RecordException(data);
        };

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ExceptionStart",
            data =>
            {
                if (!sawTypedException)
                {
                    RecordException(data);
                }
            });

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ExceptionThrown",
            data =>
            {
                if (!sawTypedException)
                {
                    RecordException(data);
                }
            });

        source.Dynamic.All += data =>
        {
            if (!string.Equals(data.ProviderName, sampleProfilerProvider, StringComparison.Ordinal))
            {
                return;
            }

            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var timeMs = data.TimeStampRelativeMSec;
            var weight = 0d;
            if (lastSampleTimeMs.HasValue)
            {
                weight = timeMs - lastSampleTimeMs.Value;
                if (weight < 0)
                {
                    weight = 0;
                }
            }
            lastSampleTimeMs = timeMs;

            var frames = EnumerateCpuFrames(stack).ToList();
            if (frames.Count == 0)
            {
                frames.Add("Unknown");
            }

            frames.Reverse();

            totalSamples++;
            callTreeTotal += weight;

            var node = callTreeRoot;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (!frameIndices.TryGetValue(frame, out var frameIdx))
                {
                    frameIdx = framesList.Count;
                    framesList.Add(frame);
                    frameIndices[frame] = frameIdx;
                }

                if (!node.Children.TryGetValue(frameIdx, out var child))
                {
                    child = new CallTreeNode(frameIdx, frame);
                    node.Children[frameIdx] = child;
                }

                if (weight > 0)
                {
                    child.Total += weight;
                }

                if (child.Calls < int.MaxValue)
                {
                    child.Calls += 1;
                }

                frameCounts.TryGetValue(frame, out var count);
                frameCounts[frame] = count + 1;

                frameTotals.TryGetValue(frame, out var total);
                frameTotals[frame] = total + weight;

                node = child;
            }

            if (weight > 0)
            {
                node.Self += weight;
            }
        };

        source.Process();

        if (totalSamples == 0)
        {
            AnsiConsole.MarkupLine($"[{theme.AccentColor}]No CPU samples found in trace.[/]");
            return null;
        }

        callTreeRoot.Total = callTreeTotal;
        callTreeRoot.Calls = totalSamples > int.MaxValue ? int.MaxValue : (int)totalSamples;

        var allFunctions = frameTotals
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                frameCounts.TryGetValue(kv.Key, out var calls);
                frameIndices.TryGetValue(kv.Key, out var frameIdx);
                return new FunctionSample(kv.Key, kv.Value, calls, frameIdx);
            })
            .ToList();

        var totalTime = frameTotals.Values.Sum();

        return new CpuProfileResult(
            allFunctions,
            totalTime,
            callTreeRoot,
            callTreeTotal,
            traceFile,
            "ms",
            "Samples",
            " samp");
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
    PrintSection(title, theme.AccentColor);
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
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.alloc.nettrace");

    var callTree = AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Collecting allocation trace for [{theme.AccentColor}]{label}[/]...", ctx =>
        {
            ctx.Status("Collecting trace data...");
            var collectArgs = new List<string>
            {
                "collect",
                "--profile",
                "gc-verbose",
                "--output",
                traceFile,
                "--"
            };
            collectArgs.AddRange(command);
            var (success, _, stderr) = RunProcess("dotnet-trace", collectArgs, timeoutMs: 180000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Allocation trace failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Analyzing allocation trace...");
            return AnalyzeAllocationTrace(traceFile);
        });

    if (callTree == null)
    {
        return null;
    }

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

ExceptionProfileResult? ExceptionProfileCommand(string[] command, string label)
{
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.exc.nettrace");
    var provider = BuildExceptionProvider();

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Collecting exception trace for [{theme.AccentColor}]{label}[/]...", ctx =>
        {
            ctx.Status("Collecting trace data...");
            var collectArgs = new List<string>
            {
                "collect",
                "--providers",
                provider,
                "--output",
                traceFile,
                "--"
            };
            collectArgs.AddRange(command);
            var (success, _, stderr) = RunProcess("dotnet-trace", collectArgs, timeoutMs: 180000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Exception trace failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Analyzing exception trace...");
            return AnalyzeExceptionTrace(traceFile);
        });
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
        var exceptionCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var typeDetails = new Dictionary<string, ExceptionTypeDetails>(StringComparer.Ordinal);
        var typeThrowRoots = new Dictionary<string, CallTreeNode>(StringComparer.Ordinal);
        var typeThrowCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var typeCatchRoots = new Dictionary<string, CallTreeNode>(StringComparer.Ordinal);
        var typeCatchCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var typeCatchSites = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        var throwFrameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var throwFramesList = new List<string>();
        var throwRoot = new CallTreeNode(-1, "Total");
        var catchFrameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var catchFramesList = new List<string>();
        var catchRoot = new CallTreeNode(-1, "Total");
        var catchSites = new Dictionary<string, long>(StringComparer.Ordinal);
        long totalThrown = 0;
        long totalCaught = 0;

        var etlxPath = traceFile;
        if (traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(traceFile);
            var targetPath = Path.Combine(outputDir, $"{fileName}.etlx");
            var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
            etlxPath = TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
        }

        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();

        var sawTypedThrow = false;
        var sawTypedCatch = false;

        void RecordThrow(TraceEvent data)
        {
            var typeName = GetExceptionTypeName(data);
            exceptionCounts[typeName] = exceptionCounts.TryGetValue(typeName, out var count)
                ? count + 1
                : 1;
            totalThrown += 1;
            RecordExceptionStack(
                data.CallStack(),
                throwRoot,
                throwFrameIndices,
                throwFramesList);

            typeThrowCounts[typeName] = typeThrowCounts.TryGetValue(typeName, out var typeCount)
                ? typeCount + 1
                : 1;
            if (!typeThrowRoots.TryGetValue(typeName, out var typeRoot))
            {
                typeRoot = new CallTreeNode(-1, "Total");
                typeThrowRoots[typeName] = typeRoot;
            }

            RecordExceptionStack(
                data.CallStack(),
                typeRoot,
                throwFrameIndices,
                throwFramesList);
        }

        void RecordCatch(TraceEvent data)
        {
            totalCaught += 1;
            RecordExceptionStack(
                data.CallStack(),
                catchRoot,
                catchFrameIndices,
                catchFramesList);

            var typeName = GetExceptionTypeName(data);
            typeCatchCounts[typeName] = typeCatchCounts.TryGetValue(typeName, out var typeCount)
                ? typeCount + 1
                : 1;
            if (!typeCatchRoots.TryGetValue(typeName, out var typeRoot))
            {
                typeRoot = new CallTreeNode(-1, "Total");
                typeCatchRoots[typeName] = typeRoot;
            }

            RecordExceptionStack(
                data.CallStack(),
                typeRoot,
                catchFrameIndices,
                catchFramesList);

            var catchSite = GetTopFrameName(data.CallStack()) ?? "Unknown";
            catchSites[catchSite] = catchSites.TryGetValue(catchSite, out var count)
                ? count + 1
                : 1;

            if (!typeCatchSites.TryGetValue(typeName, out var typeSites))
            {
                typeSites = new Dictionary<string, long>(StringComparer.Ordinal);
                typeCatchSites[typeName] = typeSites;
            }

            typeSites[catchSite] = typeSites.TryGetValue(catchSite, out var siteCount)
                ? siteCount + 1
                : 1;
        }

        source.Clr.ExceptionStart += data =>
        {
            sawTypedThrow = true;
            RecordThrow(data);
        };

        source.Clr.ExceptionCatchStart += data =>
        {
            sawTypedCatch = true;
            RecordCatch(data);
        };

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ExceptionStart",
            data =>
            {
                if (!sawTypedThrow)
                {
                    RecordThrow(data);
                }
            });

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ExceptionThrown",
            data =>
            {
                if (!sawTypedThrow)
                {
                    RecordThrow(data);
                }
            });

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ExceptionCatchStart",
            data =>
            {
                if (!sawTypedCatch)
                {
                    RecordCatch(data);
                }
            });

        source.Process();

        throwRoot.Total = totalThrown;
        throwRoot.Calls = totalThrown > int.MaxValue ? int.MaxValue : (int)totalThrown;

        CallTreeNode? catchRootResult = null;
        if (totalCaught > 0)
        {
            catchRoot.Total = totalCaught;
            catchRoot.Calls = totalCaught > int.MaxValue ? int.MaxValue : (int)totalCaught;
            catchRootResult = catchRoot;
        }

        foreach (var (typeName, count) in typeThrowCounts)
        {
            if (!typeThrowRoots.TryGetValue(typeName, out var typeRoot))
            {
                typeRoot = new CallTreeNode(-1, "Total");
                typeThrowRoots[typeName] = typeRoot;
            }

            typeRoot.Total = count;
            typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
        }

        foreach (var (typeName, count) in typeCatchCounts)
        {
            if (!typeCatchRoots.TryGetValue(typeName, out var typeRoot))
            {
                typeRoot = new CallTreeNode(-1, "Total");
                typeCatchRoots[typeName] = typeRoot;
            }

            typeRoot.Total = count;
            typeRoot.Calls = count > int.MaxValue ? int.MaxValue : (int)count;
        }

        foreach (var (typeName, thrownCount) in typeThrowCounts)
        {
            typeCatchCounts.TryGetValue(typeName, out var caughtCount);
            typeThrowRoots.TryGetValue(typeName, out var throwRootNode);
            typeCatchRoots.TryGetValue(typeName, out var catchRootNode);
            typeCatchSites.TryGetValue(typeName, out var sites);
            var siteList = (IReadOnlyList<ExceptionSiteSample>)(sites == null
                ? Array.Empty<ExceptionSiteSample>()
                : sites.OrderByDescending(kv => kv.Value)
                    .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
                    .ToList());

            if (throwRootNode != null)
            {
                typeDetails[typeName] = new ExceptionTypeDetails(
                    thrownCount,
                    throwRootNode,
                    caughtCount,
                    catchRootNode,
                    siteList);
            }
        }

        foreach (var (typeName, caughtCount) in typeCatchCounts)
        {
            if (typeDetails.ContainsKey(typeName))
            {
                continue;
            }

            typeCatchRoots.TryGetValue(typeName, out var catchRootNode);
            typeCatchSites.TryGetValue(typeName, out var sites);
            var siteList = (IReadOnlyList<ExceptionSiteSample>)(sites == null
                ? Array.Empty<ExceptionSiteSample>()
                : sites.OrderByDescending(kv => kv.Value)
                    .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
                    .ToList());

            typeDetails[typeName] = new ExceptionTypeDetails(
                0,
                new CallTreeNode(-1, "Total"),
                caughtCount,
                catchRootNode,
                siteList);
        }

        var exceptionTypes = exceptionCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ExceptionTypeSample(kv.Key, kv.Value))
            .ToList();

        var catchSiteList = catchSites
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
            .ToList();

        return new ExceptionProfileResult(
            exceptionTypes,
            throwRoot,
            totalThrown,
            typeDetails,
            catchSiteList,
            catchRootResult,
            totalCaught);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Exception trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

ContentionProfileResult? ContentionProfileCommand(string[] command, string label)
{
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.cont.nettrace");
    var keywordsValue = ClrTraceEventParser.Keywords.Contention | ClrTraceEventParser.Keywords.Threading;
    var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
    var provider = $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Collecting lock contention trace for [{theme.AccentColor}]{label}[/]...", ctx =>
        {
            ctx.Status("Collecting trace data...");
            var collectArgs = new List<string>
            {
                "collect",
                "--providers",
                provider,
                "--output",
                traceFile,
                "--"
            };
            collectArgs.AddRange(command);
            var (success, _, stderr) = RunProcess("dotnet-trace", collectArgs, timeoutMs: 180000);

            if (!success || !File.Exists(traceFile))
            {
                AnsiConsole.MarkupLine($"[{theme.ErrorColor}]Contention trace failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Analyzing contention trace...");
            return AnalyzeContentionTrace(traceFile);
        });
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
        var frameTotals = new Dictionary<string, double>(StringComparer.Ordinal);
        var frameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var frameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var framesList = new List<string>();
        var callTreeRoot = new CallTreeNode(-1, "Total");
        var pending = new Dictionary<int, Stack<(double StartTime, TraceCallStack? Stack)>>();
        var totalWaitMs = 0d;
        long totalCount = 0;

        var etlxPath = traceFile;
        if (traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(traceFile);
            var targetPath = Path.Combine(outputDir, $"{fileName}.etlx");
            var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
            etlxPath = TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
        }

        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();

        var sawTypedEvent = false;
        void HandleStart(int threadId, double timeMs, TraceCallStack? stack)
        {
            if (!pending.TryGetValue(threadId, out var stackList))
            {
                stackList = new Stack<(double StartTime, TraceCallStack? Stack)>();
                pending[threadId] = stackList;
            }

            stackList.Push((timeMs, stack));
        }

        void HandleStop(int threadId, double timeMs, double durationMs, TraceCallStack? stack)
        {
            if (pending.TryGetValue(threadId, out var stackList) && stackList.Count > 0)
            {
                var entry = stackList.Pop();
                if (stackList.Count == 0)
                {
                    pending.Remove(threadId);
                }

                if (durationMs <= 0)
                {
                    durationMs = timeMs - entry.StartTime;
                }

                stack ??= entry.Stack;
            }

            if (durationMs <= 0)
            {
                return;
            }

            var frames = EnumerateContentionFrames(stack).ToList();
            if (frames.Count == 0)
            {
                frames.Add("Unknown");
            }

            frames.Reverse();

            var node = callTreeRoot;
            foreach (var frame in frames)
            {
                if (!frameIndices.TryGetValue(frame, out var frameIdx))
                {
                    frameIdx = framesList.Count;
                    framesList.Add(frame);
                    frameIndices[frame] = frameIdx;
                }

                if (!node.Children.TryGetValue(frameIdx, out var child))
                {
                    child = new CallTreeNode(frameIdx, frame);
                    node.Children[frameIdx] = child;
                }

                child.Total += durationMs;
                if (child.Calls < int.MaxValue)
                {
                    child.Calls += 1;
                }

                frameTotals[frame] = frameTotals.TryGetValue(frame, out var total)
                    ? total + durationMs
                    : durationMs;
                frameCounts[frame] = frameCounts.TryGetValue(frame, out var count)
                    ? count + 1
                    : 1;

                node = child;
            }

            totalWaitMs += durationMs;
            totalCount += 1;
        }

        source.Clr.ContentionStart += data =>
        {
            sawTypedEvent = true;
            HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack());
        };

        source.Clr.ContentionStop += data =>
        {
            sawTypedEvent = true;
            var durationMs = data.DurationNs > 0
                ? data.DurationNs / 1_000_000d
                : 0d;
            HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
        };

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ContentionStart_V2",
            data =>
            {
                HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack());
            });

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ContentionStop_V2",
            data =>
            {
                var durationMs = TryGetPayloadDurationMs(data);
                HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
            });

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ContentionStart",
            data =>
            {
                if (sawTypedEvent)
                {
                    return;
                }

                HandleStart(data.ThreadID, data.TimeStampRelativeMSec, data.CallStack());
            });

        source.Dynamic.AddCallbackForProviderEvent(
            "Microsoft-Windows-DotNETRuntime",
            "ContentionStop",
            data =>
            {
                if (sawTypedEvent)
                {
                    return;
                }

                var durationMs = TryGetPayloadDurationMs(data);
                HandleStop(data.ThreadID, data.TimeStampRelativeMSec, durationMs, data.CallStack());
            });

        source.Process();

        callTreeRoot.Total = totalWaitMs;
        callTreeRoot.Calls = totalCount > int.MaxValue ? int.MaxValue : (int)totalCount;

        var topFunctions = frameTotals
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                frameCounts.TryGetValue(kv.Key, out var calls);
                frameIndices.TryGetValue(kv.Key, out var frameIdx);
                return new FunctionSample(kv.Key, kv.Value, calls, frameIdx);
            })
            .ToList();

        return new ContentionProfileResult(topFunctions, callTreeRoot, totalWaitMs, totalCount);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Contention trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

IEnumerable<string> EnumerateContentionFrames(TraceCallStack? stack)
{
    if (stack == null)
    {
        yield break;
    }

    for (var current = stack; current != null; current = current.Caller)
    {
        var methodName = current.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = current.CodeAddress?.Method?.FullMethodName;
        }

        if (!string.IsNullOrWhiteSpace(methodName))
        {
            yield return methodName;
        }
    }
}

void RecordExceptionStack(
    TraceCallStack? stack,
    CallTreeNode root,
    Dictionary<string, int> frameIndices,
    List<string> framesList)
{
    var frames = EnumerateExceptionFrames(stack).ToList();
    if (frames.Count == 0)
    {
        frames.Add("Unknown");
    }

    frames.Reverse();
    var node = root;
    foreach (var frame in frames)
    {
        if (!frameIndices.TryGetValue(frame, out var frameIdx))
        {
            frameIdx = framesList.Count;
            framesList.Add(frame);
            frameIndices[frame] = frameIdx;
        }

        if (!node.Children.TryGetValue(frameIdx, out var child))
        {
            child = new CallTreeNode(frameIdx, frame);
            node.Children[frameIdx] = child;
        }

        child.Total += 1;
        if (child.Calls < int.MaxValue)
        {
            child.Calls += 1;
        }

        node = child;
    }
}

IEnumerable<string> EnumerateExceptionFrames(TraceCallStack? stack)
{
    if (stack == null)
    {
        yield break;
    }

    for (var current = stack; current != null; current = current.Caller)
    {
        var methodName = current.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = current.CodeAddress?.Method?.FullMethodName;
        }

        if (!string.IsNullOrWhiteSpace(methodName))
        {
            yield return methodName;
        }
    }
}

IEnumerable<string> EnumerateCpuFrames(TraceCallStack? stack)
{
    if (stack == null)
    {
        yield break;
    }

    var lastWasUnknown = false;
    for (var current = stack; current != null; current = current.Caller)
    {
        var methodName = current.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = current.CodeAddress?.Method?.FullMethodName;
        }

        if (string.IsNullOrWhiteSpace(methodName))
        {
            if (!lastWasUnknown)
            {
                yield return "Unmanaged Code";
                lastWasUnknown = true;
            }

            continue;
        }

        lastWasUnknown = false;
        yield return methodName;
    }
}

string? GetTopFrameName(TraceCallStack? stack)
{
    if (stack == null)
    {
        return null;
    }

    var methodName = stack.CodeAddress?.FullMethodName;
    if (string.IsNullOrWhiteSpace(methodName))
    {
        methodName = stack.CodeAddress?.Method?.FullMethodName;
    }

    return string.IsNullOrWhiteSpace(methodName) ? null : methodName;
}

string GetExceptionTypeName(TraceEvent data)
{
    var typeName = TryGetPayloadString(data, "ExceptionTypeName", "ExceptionType", "TypeName");
    if (string.IsNullOrWhiteSpace(typeName))
    {
        try
        {
            foreach (var payloadName in data.PayloadNames ?? Array.Empty<string>())
            {
                if (!payloadName.Contains("ExceptionType", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = data.PayloadByName(payloadName);
                if (value != null)
                {
                    typeName = value.ToString();
                    break;
                }
            }
        }
        catch
        {
            typeName = null;
        }
    }

    return string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName;
}

double TryGetPayloadDurationMs(TraceEvent data)
{
    var durationNs = TryGetPayloadLong(data, "DurationNs")
                     ?? TryGetPayloadLong(data, "DurationNS")
                     ?? TryGetPayloadLong(data, "Duration");
    if (durationNs is > 0)
    {
        return durationNs.Value / 1_000_000d;
    }

    return 0d;
}

string? TryGetPayloadString(TraceEvent data, params string[] names)
{
    foreach (var name in names)
    {
        try
        {
            var value = data.PayloadByName(name);
            if (value != null)
            {
                return value.ToString();
            }
        }
        catch
        {
            // Ignore missing payloads.
        }
    }

    return null;
}

long? TryGetPayloadLong(TraceEvent data, string name)
{
    try
    {
        var value = data.PayloadByName(name);
        if (value == null)
        {
            return null;
        }

        return value switch
        {
            byte v => v,
            sbyte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v <= long.MaxValue ? (long)v : null,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }
    catch
    {
        return null;
    }
}

AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
{
    try
    {
        var typeRoots = new Dictionary<string, AllocationCallTreeNode>(StringComparer.Ordinal);
        long totalBytes = 0;
        long totalCount = 0;

        var etlxPath = traceFile;
        if (traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(traceFile);
            var targetPath = Path.Combine(outputDir, $"{fileName}.etlx");
            var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
            etlxPath = TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
        }

        using var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        using var source = traceLog.Events.GetSource();
        source.Clr.GCAllocationTick += data =>
        {
            var bytes = data.AllocationAmount64;
            if (bytes <= 0)
            {
                return;
            }

            var typeName = string.IsNullOrWhiteSpace(data.TypeName) ? "Unknown" : data.TypeName;
            if (!typeRoots.TryGetValue(typeName, out var typeRoot))
            {
                typeRoot = new AllocationCallTreeNode(typeName);
                typeRoots[typeName] = typeRoot;
            }

            totalBytes += bytes;
            totalCount++;
            typeRoot.TotalBytes += bytes;
            typeRoot.Count++;

            var stack = data.CallStack();
            if (stack == null)
            {
                return;
            }

            var node = typeRoot;
            foreach (var frame in EnumerateAllocationFrames(stack))
            {
                if (string.IsNullOrWhiteSpace(frame))
                {
                    continue;
                }

                if (!node.Children.TryGetValue(frame, out var child))
                {
                    child = new AllocationCallTreeNode(frame);
                    node.Children[frame] = child;
                }

                child.TotalBytes += bytes;
                child.Count++;
                node = child;
            }
        };

        source.Process();

        var roots = typeRoots.Values
            .OrderByDescending(node => node.TotalBytes)
            .ToList();

        return new AllocationCallTreeResult(totalBytes, totalCount, roots);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Allocation trace parse failed:[/] {Markup.Escape(ex.Message)}");
        return null;
    }
}

IEnumerable<string> EnumerateAllocationFrames(TraceCallStack stack)
{
    for (var current = stack; current != null; current = current.Caller)
    {
        var methodName = current.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = current.CodeAddress?.Method?.FullMethodName;
        }

        yield return methodName ?? "Unknown";
    }
}

void AddAllocationCallTreeChildren(
    TreeNode parent,
    AllocationCallTreeNode node,
    long rootTotalBytes,
    bool includeRuntime,
    int depth,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    if (depth > maxDepth)
    {
        return;
    }

    var children = GetVisibleAllocationChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
    foreach (var child in children)
    {
        var nextDepth = depth + 1;
        var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
        var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
            ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
            : Array.Empty<AllocationCallTreeNode>();
        var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

        var childNode = parent.AddNode(FormatAllocationCallTreeLine(child, rootTotalBytes, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddAllocationCallTreeChildren(
                childNode,
                child,
                rootTotalBytes,
                includeRuntime,
                nextDepth,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }
}

IReadOnlyList<AllocationCallTreeNode> GetVisibleAllocationChildren(
    AllocationCallTreeNode node,
    bool includeRuntime,
    int maxWidth,
    int siblingCutoffPercent)
{
    var ordered = EnumerateVisibleAllocationChildren(node, includeRuntime)
        .OrderByDescending(child => child.TotalBytes)
        .ToList();

    if (ordered.Count == 0)
    {
        return ordered;
    }

    if (siblingCutoffPercent <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var topBytes = ordered[0].TotalBytes;
    if (topBytes <= 0)
    {
        return ordered.Take(maxWidth).ToList();
    }

    var minBytes = topBytes * siblingCutoffPercent / 100d;
    return ordered
        .Where(child => child.TotalBytes >= minBytes)
        .Take(maxWidth)
        .ToList();
}

IEnumerable<AllocationCallTreeNode> EnumerateVisibleAllocationChildren(
    AllocationCallTreeNode node,
    bool includeRuntime)
{
    foreach (var child in node.Children.Values)
    {
        if (includeRuntime || !IsRuntimeNoise(child.Name))
        {
            yield return child;
            continue;
        }

        foreach (var grandChild in EnumerateVisibleAllocationChildren(child, includeRuntime))
        {
            yield return grandChild;
        }
    }
}

string FormatAllocationCallTreeLine(
    AllocationCallTreeNode node,
    long rootTotalBytes,
    bool isRoot,
    bool isLeaf)
{
    var bytes = node.TotalBytes;
    var pct = rootTotalBytes > 0 ? 100d * bytes / rootTotalBytes : 0d;
    var count = node.Count;
    var bytesText = FormatBytes(bytes);
    var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
    var countText = count.ToString("N0", CultureInfo.InvariantCulture);

    var displayName = isRoot ? NameFormatter.FormatTypeDisplayName(node.Name) : FormatFunctionDisplayName(node.Name);
    if (displayName.Length > 80)
    {
        displayName = displayName[..77] + "...";
    }

    var nameText = isRoot
        ? $"[{theme.TextColor}]{Markup.Escape(displayName)}[/]"
        : FormatCallTreeName(displayName, displayName, isLeaf);

    return $"[{theme.CpuValueColor}]{bytesText}[/] [{theme.SampleColor}]{pctText}%[/] [{theme.CpuCountColor}]{countText}x[/] {nameText}";
}

double GetCallTreeTime(CallTreeNode node, bool useSelfTime)
{
    return useSelfTime ? node.Self : node.Total;
}

double ComputeHotness(CallTreeNode node, double totalTime, double totalSamples)
{
    if (totalTime <= 0 || totalSamples <= 0)
    {
        return 0;
    }

    var sampleRatio = node.Calls / totalSamples;
    var selfRatio = node.Self / totalTime;
    return sampleRatio * selfRatio;
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

IReadOnlyList<(string Filter, string DisplayName, double Hotness)> CollectHotMethods(
    CallTreeNode rootNode,
    double totalTime,
    double totalSamples,
    bool includeRuntime,
    double hotThreshold)
{
    var hotMethods = new Dictionary<string, (string DisplayName, double Hotness)>(StringComparer.OrdinalIgnoreCase);
    if (totalTime <= 0 || totalSamples <= 0)
    {
        return Array.Empty<(string Filter, string DisplayName, double Hotness)>();
    }

    void Visit(CallTreeNode node)
    {
        if (node.FrameIdx >= 0 &&
            (includeRuntime || !IsRuntimeNoise(node.Name)))
        {
            var matchName = GetCallTreeMatchName(node);
            if (!IsUnmanagedFrame(matchName))
            {
                var hotness = ComputeHotness(node, totalTime, totalSamples);
                if (hotness >= hotThreshold)
                {
                    var filterName = BuildJitMethodFilter(node.Name);
                    if (!hotMethods.TryGetValue(filterName, out var existing) || hotness > existing.Hotness)
                    {
                        hotMethods[filterName] = (matchName, hotness);
                    }
                }
            }
        }

        foreach (var child in node.Children.Values)
        {
            Visit(child);
        }
    }

    Visit(rootNode);

    return hotMethods
        .Select(entry => (Filter: entry.Key, DisplayName: entry.Value.DisplayName, Hotness: entry.Value.Hotness))
        .OrderByDescending(entry => entry.Hotness)
        .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

List<CallTreeMatch> FindCallTreeMatches(CallTreeNode node, string filter)
{
    var matches = new List<CallTreeMatch>();
    var normalizedFilter = filter.Trim();
    if (normalizedFilter.Length == 0)
    {
        return matches;
    }

    var order = 0;
    void Visit(CallTreeNode current, int depth)
    {
        if (current.FrameIdx >= 0)
        {
            var displayName = FormatFunctionDisplayName(current.Name);
            if (displayName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                current.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new CallTreeMatch(current, depth, order++));
            }
        }

        foreach (var child in current.Children.Values)
        {
            Visit(child, depth + 1);
        }
    }

    Visit(node, 0);
    return matches;
}

CallTreeNode SelectRootMatch(List<CallTreeMatch> matches, bool includeRuntime, string? rootMode)
{
    if (matches.Count == 0)
    {
        throw new InvalidOperationException("No call tree matches available.");
    }

    var mode = NormalizeRootMode(rootMode);
    var candidates = includeRuntime
        ? matches
        : matches.Where(match => !IsRuntimeNoise(match.Node.Name)).ToList();
    if (candidates.Count == 0)
    {
        candidates = matches;
    }

    return mode switch
    {
        "first" or "shallowest" => candidates
            .OrderBy(match => match.Depth)
            .ThenBy(match => match.Order)
            .Select(match => match.Node)
            .First(),
        _ => candidates
            .OrderByDescending(match => GetCallTreeTime(match.Node, useSelfTime: false))
            .Select(match => match.Node)
            .First()
    };
}

string NormalizeRootMode(string? rootMode)
{
    if (string.IsNullOrWhiteSpace(rootMode))
    {
        return "hottest";
    }

    return rootMode.Trim().ToLowerInvariant();
}

string FormatBytes(long bytes)
{
    if (bytes < 1024)
    {
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    if (bytes < 1024 * 1024)
    {
        return (bytes / 1024d).ToString("F2", CultureInfo.InvariantCulture) + " KB";
    }

    if (bytes < 1024L * 1024L * 1024L)
    {
        return (bytes / (1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
    }

    return (bytes / (1024d * 1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " GB";
}

bool IsUnmanagedFrame(string name)
{
    var trimmed = name?.Trim() ?? string.Empty;
    if (trimmed.Length == 0)
    {
        return false;
    }

    if (trimmed.Contains("UNMANAGED_CODE_TIME", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Contains("Unmanaged Code", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    foreach (var ch in trimmed)
    {
        if (char.IsLetter(ch))
        {
            return false;
        }
    }

    return true;
}

string FormatFunctionDisplayName(string rawName)
{
    var formatted = NameFormatter.FormatMethodDisplayName(rawName);
    return GetCallTreeDisplayName(formatted);
}

string FormatCallTreeName(string displayName, string matchName, bool isLeaf, string? nameColorOverride = null)
{
    var escaped = Markup.Escape(displayName);
    if (isLeaf && ShouldStopAtLeaf(matchName))
    {
        return $"[{theme.LeafHighlightColor}]{escaped}[/]";
    }

    var color = string.IsNullOrWhiteSpace(nameColorOverride) ? theme.TextColor : nameColorOverride;
    return $"[{color}]{escaped}[/]";
}

string GetCallTreeMatchName(CallTreeNode node)
{
    return NameFormatter.FormatMethodDisplayName(node.Name);
}

string GetCallTreeDisplayName(string matchName)
{
    if (IsUnmanagedFrame(matchName))
    {
        return "Unmanaged Code";
    }

    return matchName;
}

string BuildJitMethodFilter(string rawName)
{
    if (string.IsNullOrWhiteSpace(rawName))
    {
        return rawName;
    }

    var trimmed = rawName.Trim();
    var paramIndex = trimmed.IndexOf('(');
    if (paramIndex >= 0)
    {
        trimmed = trimmed[..paramIndex];
    }

    trimmed = trimmed.Trim();
    trimmed = trimmed.Replace('+', '.');

    var lastDot = trimmed.LastIndexOf('.');
    if (lastDot > 0 && lastDot < trimmed.Length - 1)
    {
        return $"{trimmed[..lastDot]}:{trimmed[(lastDot + 1)..]}";
    }

    return trimmed;
}

bool ShouldStopAtLeaf(string matchName)
{
    return IsUnmanagedFrame(matchName) ||
           matchName.Contains("CastHelpers.", StringComparison.Ordinal) ||
           matchName.Contains("Array.Copy", StringComparison.Ordinal) ||
           matchName.Contains("Dictionary<__Canon,__Canon>.Resize", StringComparison.Ordinal) ||
           matchName.Contains("Buffer.BulkMoveWithWriteBarrier", StringComparison.Ordinal) ||
           matchName.Contains("SpanHelpers.SequenceEqual", StringComparison.Ordinal) ||
           matchName.Contains("HashSet<", StringComparison.Ordinal) ||
           matchName.Contains("Enumerable+ArrayWhereSelectIterator<", StringComparison.Ordinal) ||
           matchName.Contains("ImmutableDictionary<", StringComparison.Ordinal) ||
           matchName.Contains("SegmentedArrayBuilder<__Canon>.ToArray", StringComparison.Ordinal) ||
           matchName.Contains("__Canon", StringComparison.Ordinal) ||
           (matchName.Contains("List<", StringComparison.Ordinal) &&
            matchName.EndsWith(".ToArray", StringComparison.Ordinal));
}

bool IsRuntimeNoise(string name)
{
    var trimmed = name.TrimStart();
    var formatted = FormatFunctionDisplayName(trimmed);
    return IsUnmanagedFrame(trimmed) ||
           trimmed.Contains("(Non-Activities)", StringComparison.Ordinal) ||
           trimmed.Contains("Thread", StringComparison.Ordinal) ||
           trimmed.Contains("Threads", StringComparison.Ordinal) ||
           trimmed.Contains("Process", StringComparison.Ordinal) ||
           StartsWithDigits(trimmed) ||
           StartsWithDigits(formatted);
}

bool StartsWithDigits(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        return false;
    }

    var trimmed = name.TrimStart();
    return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
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

    var (success, _, stderr) = RunProcess(
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

    var (reportSuccess, reportOut, reportErr) = RunProcess(
        "dotnet-gcdump",
        new[] { "report", gcdumpFile },
        timeoutMs: 60000);

    if (reportSuccess)
    {
        return ParseGcdumpReport(reportOut);
    }

    AnsiConsole.MarkupLine($"[{theme.AccentColor}]Could not parse gcdump, showing raw output:[/] {Markup.Escape(reportErr)}");
    return new HeapProfileResult(reportOut, Array.Empty<HeapTypeEntry>());
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

    proc.OutputDataReceived += (_, e) =>
    {
        if (e.Data == null)
        {
            return;
        }

        stdoutWriter.WriteLine(e.Data);
    };
    proc.ErrorDataReceived += (_, e) =>
    {
        if (e.Data == null)
        {
            return;
        }

        stderrWriter.WriteLine(e.Data);
    };

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

    proc.OutputDataReceived += (_, e) =>
    {
        if (e.Data == null)
        {
            return;
        }

        stdoutWriter.WriteLine(e.Data);
        colorWriter.WriteLine(ColorizeJitDisasmLine(e.Data));
    };
    proc.ErrorDataReceived += (_, e) =>
    {
        if (e.Data == null)
        {
            return;
        }

        stderrWriter.WriteLine(e.Data);
    };

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

    if (line.StartsWith(";", StringComparison.Ordinal))
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
    if (TryParseHexColor(value, out rgb))
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
                if (tierStart > 0 && methodLine.EndsWith(")", StringComparison.Ordinal))
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

    PrintSection("JIT DISASM SUMMARY", theme.AccentColor);
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

    PrintSection("JIT INLINE SUMMARY", theme.AccentColor);
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

        var (reportSuccess, reportOut, reportErr) = RunProcess(
            "dotnet-gcdump",
            new[] { "report", inputPath },
            timeoutMs: 60000);

        if (reportSuccess)
        {
            return ParseGcdumpReport(reportOut);
        }

        AnsiConsole.MarkupLine($"[{theme.AccentColor}]Could not parse gcdump, showing raw output:[/] {Markup.Escape(reportErr)}");
        return new HeapProfileResult(reportOut, Array.Empty<HeapTypeEntry>());
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


    var resolver = new ProjectResolver(RunProcess);
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
            renderer.PrintCpuResults(
                cpuResults,
                label,
                description,
                callTreeRoot,
                functionFilter,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeRootMode,
                callTreeSelf,
                callTreeSiblingCutoff,
                hotThreshold,
                timeline,
                timelineWidth,
                memoryResults: memoryResults);
            if ((jitDisasmHot || hotThresholdSpecified) && jit)
            {
                RunHotJitDisasm(cpuResults, command, callTreeRoot, callTreeRootMode, includeRuntime, hotThreshold);
            }
        }
        else if (memoryResults != null)
        {
            renderer.PrintMemoryResults(
                memoryResults,
                label,
                description,
                callTreeRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeSiblingCutoff);
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
            renderer.PrintCpuResults(
                results,
                label,
                description,
                callTreeRoot,
                functionFilter,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeRootMode,
                callTreeSelf,
                callTreeSiblingCutoff,
                hotThreshold,
                timeline,
                timelineWidth);
            if ((jitDisasmHot || hotThresholdSpecified) && jit && results != null)
            {
                RunHotJitDisasm(results, command, callTreeRoot, callTreeRootMode, includeRuntime, hotThreshold);
            }
        }

        if (runMemory)
        {
            Console.WriteLine($"{label} - memory");
            var results = hasInput
                ? MemoryProfileFromInput(inputPath!, label)
                : sharedTraceFile != null
                    ? MemoryProfileFromInput(sharedTraceFile, label)
                    : MemoryProfileCommand(command, label);
            renderer.PrintMemoryResults(
                results,
                label,
                description,
                callTreeRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeSiblingCutoff);
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
        renderer.PrintExceptionResults(
            results,
            label,
            description,
            callTreeRoot,
            exceptionTypeFilter,
            functionFilter,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoff);
    }

    if (runContention)
    {
        Console.WriteLine($"{label} - contention");
        var results = hasInput
            ? ContentionProfileFromInput(inputPath!, label)
            : ContentionProfileCommand(command, label);
        renderer.PrintContentionResults(
            results,
            label,
            description,
            callTreeRoot,
            functionFilter,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoff);
    }

    if (runHeap)
    {
        Console.WriteLine($"{label} - heap");
        var results = hasInput
            ? HeapProfileFromInput(inputPath!)
            : HeapProfileCommand(command, label);
        renderer.PrintHeapResults(results, label, description);
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
