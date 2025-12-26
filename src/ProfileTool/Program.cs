using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Threading;
using Asynkron.Profiler;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;
using Spectre.Console.Rendering;

void PrintSection(string text)
{
    Console.WriteLine();
    Console.WriteLine(text);
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
        "Render existing traces:",
        "  asynkron-profiler --input ./profile-output/app.nettrace",
        "  asynkron-profiler --input ./profile-output/app.speedscope.json --cpu",
        "  asynkron-profiler --input ./profile-output/app.etlx --memory",
        "  asynkron-profiler --input ./profile-output/app.nettrace --exception",
        "",
        "General:",
        "  asynkron-profiler --help"
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
        AnsiConsole.MarkupLine($"[red]{toolName} unavailable:[/] {Markup.Escape(detail)}");
        AnsiConsole.MarkupLine($"[yellow]Install:[/] {Markup.Escape(installHint)}");
        toolAvailability[toolName] = false;
        return false;
    }

    toolAvailability[toolName] = true;
    return true;
}

CpuProfileResult? CpuProfileCommand(string[] command, string label)
{
    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    var traceFile = Path.Combine(outputDir, $"{label}_{timestamp}.nettrace");
    var speedscopeBase = Path.Combine(outputDir, $"{label}_{timestamp}");
    var speedscopeFile = speedscopeBase + ".speedscope.json";

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Running CPU profile on [yellow]{label}[/]...", ctx =>
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
                AnsiConsole.MarkupLine($"[red]Trace collection failed:[/] {Markup.Escape(stderr)}");
                return null;
            }

            ctx.Status("Converting trace to speedscope format...");
            var convertArgs = new[]
            {
                "convert",
                traceFile,
                "--format",
                "Speedscope",
                "--output",
                speedscopeBase
            };
            var convert = RunProcess("dotnet-trace", convertArgs, timeoutMs: 120000);
            if (!convert.Success || !File.Exists(speedscopeFile))
            {
                AnsiConsole.MarkupLine($"[red]Speedscope conversion failed:[/] {Markup.Escape(convert.StdErr)}");
                return null;
            }

            ctx.Status("Analyzing profile data...");
            return AnalyzeSpeedscope(speedscopeFile);
        });
}

CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
{
    var json = File.ReadAllText(speedscopePath);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    var frames = root.GetProperty("shared").GetProperty("frames");
    var profile = root.GetProperty("profiles")[0];

    var framesList = new List<string>();
    foreach (var frame in frames.EnumerateArray())
    {
        framesList.Add(frame.GetProperty("name").GetString() ?? "Unknown");
    }

    var frameTimes = new Dictionary<int, double>();
    var frameSelfTimes = new Dictionary<int, double>();
    var frameCounts = new Dictionary<int, int>();
    var callTreeRoot = new CallTreeNode(-1, "Total");
    var stack = new List<(CallTreeNode Node, double Start, int FrameIdx)>();
    var hasLast = false;
    var lastAt = 0d;
    var callTreeTotal = 0d;

    if (profile.TryGetProperty("events", out var events))
    {
        foreach (var evt in events.EnumerateArray())
        {
            var eventType = evt.GetProperty("type").GetString();
            var frameIdx = evt.GetProperty("frame").GetInt32();
            var at = evt.GetProperty("at").GetDouble();

            if (hasLast && stack.Count > 0)
            {
                var topIdx = stack[^1].FrameIdx;
                frameSelfTimes.TryGetValue(topIdx, out var selfTime);
                var delta = at - lastAt;
                frameSelfTimes[topIdx] = selfTime + delta;
                stack[^1].Node.Self += delta;
            }

            hasLast = true;
            lastAt = at;

            if (string.Equals(eventType, "O", StringComparison.Ordinal)) // Open
            {
                var parentNode = stack.Count > 0 ? stack[^1].Node : callTreeRoot;
                var childNode = GetOrCreateCallTreeChild(parentNode, frameIdx, framesList);
                childNode.Calls += 1;
                stack.Add((childNode, at, frameIdx));
                frameCounts.TryGetValue(frameIdx, out var count);
                frameCounts[frameIdx] = count + 1;
            }
            else if (string.Equals(eventType, "C", StringComparison.Ordinal)) // Close
            {
                if (stack.Count > 0 && stack[^1].FrameIdx == frameIdx)
                {
                    var (node, openTime, _) = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    var duration = at - openTime;
                    frameTimes.TryGetValue(frameIdx, out var time);
                    frameTimes[frameIdx] = time + duration;
                    node.Total += duration;
                    if (stack.Count == 0)
                    {
                        callTreeTotal += duration;
                    }
                }
            }
        }
    }

    var allFunctions = new List<FunctionSample>();
    double totalTime = frameTimes.Values.Sum();

    if (callTreeTotal <= 0)
    {
        callTreeTotal = SumCallTreeTotals(callTreeRoot);
    }
    callTreeRoot.Total = callTreeTotal;
    callTreeRoot.Calls = SumCallTreeCalls(callTreeRoot);

    foreach (var (frameIdx, timeSpent) in frameTimes.OrderByDescending(kv => kv.Value))
    {
        var name = frameIdx < framesList.Count ? framesList[frameIdx] : "Unknown";
        frameCounts.TryGetValue(frameIdx, out var calls);

        var entry = new FunctionSample(name, timeSpent, calls, frameIdx);
        allFunctions.Add(entry);

        _ = name;
    }

    return new CpuProfileResult(
        allFunctions,
        totalTime,
        callTreeRoot,
        callTreeTotal,
        speedscopePath);
}

void PrintCpuResults(
    CpuProfileResult? results,
    string profileName,
    string? description,
    string? rootFilter,
    string? functionFilter,
    bool includeRuntime,
    int callTreeDepth,
    int callTreeWidth,
    string? callTreeRootMode,
    bool showSelfTimeTree,
    int callTreeSiblingCutoffPercent,
    bool showTimeline = false,
    int timelineWidth = 60,
    int timelineMaxSpans = 100,
    double timelineMinDuration = 0.1)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    PrintSection($"CPU PROFILE: {profileName}");
    if (!string.IsNullOrWhiteSpace(description))
    {
        AnsiConsole.MarkupLine($"[dim]{description}[/]");
    }

    // Show timeline view if requested
    if (showTimeline)
    {
        if (string.IsNullOrWhiteSpace(results.SpeedscopePath))
        {
            AnsiConsole.MarkupLine("[red]Timeline view requires speedscope data (not available)[/]");
        }
        else
        {
            PrintSection("TIMELINE VIEW");
            var spans = CpuTimelineFormatter.ParseSpeedscopeSpans(results.SpeedscopePath);

            // Apply runtime filter if needed
            Func<CpuTimelineFormatter.ProfileSpan, bool>? predicate = null;
            if (!includeRuntime)
            {
                predicate = span => !IsRuntimeNoise(span.Name);
            }

            var timeline = CpuTimelineFormatter.Format(
                spans,
                width: timelineWidth,
                maxSpans: timelineMaxSpans,
                minDurationMs: timelineMinDuration,
                predicate: predicate);

            Console.WriteLine(timeline);
        }
    }

    var allFunctions = results.AllFunctions;
    var totalTime = results.TotalTime;

    // Top functions overall - using Spectre table
    AnsiConsole.WriteLine();
    var filteredAll = allFunctions.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
    if (!includeRuntime)
    {
        filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
    }
    var filteredList = filteredAll.ToList();

    var topTitle = includeRuntime && string.IsNullOrWhiteSpace(functionFilter)
        ? "Top Functions (All)"
        : "Top Functions (Filtered)";
    var table = new Table()
        .Border(TableBorder.None)
        .Title($"[bold]{topTitle}[/]")
        .AddColumn(new TableColumn("[yellow]Time (ms)[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Calls[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Function[/]"));

    foreach (var entry in filteredList.Take(15))
    {
        var funcName = NameFormatter.FormatMethodDisplayName(entry.Name);
        if (funcName.Length > 70) funcName = funcName[..67] + "...";

        var timeMs = entry.TimeMs;
        var calls = entry.Calls;
        var timeMsText = timeMs.ToString("F2", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);

        table.AddRow(
            $"[green]{timeMsText}[/]",
            $"[blue]{callsText}[/]",
            Markup.Escape(funcName)
        );
    }

    AnsiConsole.Write(table);
    var filteredOut = allFunctions.Count - filteredList.Count;
    if (filteredOut > 0)
    {
        var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine(
            $"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
    }
    if (!string.IsNullOrWhiteSpace(functionFilter))
    {
        AnsiConsole.MarkupLine(
            $"[dim]Filter: {Markup.Escape(functionFilter)} (use --filter to change).[/]");
    }

    // Summary panel
    AnsiConsole.WriteLine();

    var summaryTable = new Table()
        .Border(TableBorder.None)
        .Title("[bold yellow]Summary[/]")
        .HideHeaders()
        .AddColumn("")
        .AddColumn("");

    var totalTimeText = totalTime.ToString("F2", CultureInfo.InvariantCulture);
    var hotCountText = allFunctions.Count.ToString(CultureInfo.InvariantCulture);
    summaryTable.AddRow("[bold]Total Time[/]", $"[green]{totalTimeText} ms[/]");
    summaryTable.AddRow("[bold]Hot Functions[/]", $"[blue]{hotCountText}[/] functions profiled");

    AnsiConsole.Write(summaryTable);

    var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
    AnsiConsole.Write(BuildCallTree(
        results,
        useSelfTime: false,
        resolvedRoot,
        includeRuntime,
        callTreeDepth,
        callTreeWidth,
        callTreeRootMode,
        callTreeSiblingCutoffPercent));
    if (showSelfTimeTree)
    {
        AnsiConsole.Write(BuildCallTree(
            results,
            useSelfTime: true,
            resolvedRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoffPercent));
    }
}

CpuProfileResult? CpuProfileFromInput(string inputPath, string label)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[red]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension == ".json")
    {
        return AnalyzeSpeedscope(inputPath);
    }

    if (extension != ".nettrace")
    {
        AnsiConsole.MarkupLine($"[red]Unsupported CPU input:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
    {
        return null;
    }

    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    var speedscopeBase = Path.Combine(outputDir, $"{label}_{timestamp}");
    var speedscopeFile = speedscopeBase + ".speedscope.json";
    var convertArgs = new[]
    {
        "convert",
        inputPath,
        "--format",
        "Speedscope",
        "--output",
        speedscopeBase
    };
    var convert = RunProcess("dotnet-trace", convertArgs, timeoutMs: 120000);
    if (!convert.Success || !File.Exists(speedscopeFile))
    {
        AnsiConsole.MarkupLine($"[red]Speedscope conversion failed:[/] {Markup.Escape(convert.StdErr)}");
        return null;
    }

    return AnalyzeSpeedscope(speedscopeFile);
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
        .Start($"Collecting allocation trace for [yellow]{label}[/]...", ctx =>
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
                AnsiConsole.MarkupLine($"[red]Allocation trace failed:[/] {Markup.Escape(stderr)}");
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
        AnsiConsole.MarkupLine($"[red]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[red]Unsupported memory input:[/] {Markup.Escape(inputPath)}");
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
    var keywordsValue = ClrTraceEventParser.Keywords.Exception;
    var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
    var provider = $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";

    return AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start($"Collecting exception trace for [yellow]{label}[/]...", ctx =>
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
                AnsiConsole.MarkupLine($"[red]Exception trace failed:[/] {Markup.Escape(stderr)}");
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
        AnsiConsole.MarkupLine($"[red]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[red]Unsupported exception input:[/] {Markup.Escape(inputPath)}");
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
        AnsiConsole.MarkupLine($"[yellow]Exception trace parse failed:[/] {Markup.Escape(ex.Message)}");
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
        .Start($"Collecting lock contention trace for [yellow]{label}[/]...", ctx =>
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
                AnsiConsole.MarkupLine($"[red]Contention trace failed:[/] {Markup.Escape(stderr)}");
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
        AnsiConsole.MarkupLine($"[red]Input file not found:[/] {Markup.Escape(inputPath)}");
        return null;
    }

    var extension = Path.GetExtension(inputPath).ToLowerInvariant();
    if (extension != ".nettrace" && extension != ".etlx")
    {
        AnsiConsole.MarkupLine($"[red]Unsupported contention input:[/] {Markup.Escape(inputPath)}");
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
        AnsiConsole.MarkupLine($"[yellow]Contention trace parse failed:[/] {Markup.Escape(ex.Message)}");
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
        AnsiConsole.MarkupLine($"[yellow]Allocation trace parse failed:[/] {Markup.Escape(ex.Message)}");
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

void PrintMemoryResults(
    MemoryProfileResult? results,
    string profileName,
    string? description,
    string? callTreeRoot,
    bool includeRuntime,
    int callTreeDepth,
    int callTreeWidth,
    int callTreeSiblingCutoffPercent)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    PrintSection($"MEMORY PROFILE: {profileName}");
    if (!string.IsNullOrWhiteSpace(description))
    {
        AnsiConsole.MarkupLine($"[dim]{description}[/]");
    }

    var table = new Table()
        .Border(TableBorder.None)
        .AddColumn(new TableColumn("[yellow]Metric[/]"))
        .AddColumn(new TableColumn("[yellow]Value[/]"));

    var hasRows = false;

    void AddRow(string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            table.AddRow(label, Markup.Escape(value));
            hasRows = true;
        }
    }

    AddRow("Iterations", results.Iterations);
    AddRow("Total time", results.TotalTime);
    AddRow("Per iteration (time)", results.PerIterationTime);
    AddRow("Total allocated", results.TotalAllocated);
    AddRow("Per iteration (allocated)", results.PerIterationAllocated);
    AddRow("GC Gen0 collections", results.Gen0Collections);
    AddRow("GC Gen1 collections", results.Gen1Collections);
    AddRow("GC Gen2 collections", results.Gen2Collections);
    AddRow("Parse (allocated)", results.ParseAllocated);
    AddRow("Evaluate (allocated)", results.EvaluateAllocated);
    AddRow("Heap before", results.HeapBefore);
    AddRow("Heap after", results.HeapAfter);

    if (hasRows)
    {
        AnsiConsole.Write(table);
    }

    if (!string.IsNullOrWhiteSpace(results.AllocationByTypeRaw))
    {
        PrintSection("Allocation By Type (Sampled)");
        AnsiConsole.WriteLine(results.AllocationByTypeRaw);
    }
    else if (results.AllocationEntries.Count > 0)
    {
        PrintSection("Allocation By Type (Sampled)");
        PrintAllocationTable(results.AllocationEntries, results.AllocationTotal);
    }
    else if (!hasRows && !string.IsNullOrWhiteSpace(results.RawOutput))
    {
        AnsiConsole.WriteLine(results.RawOutput);
    }

    if (results.AllocationCallTree != null)
    {
        PrintSection("Allocation Call Tree (Sampled)");
        PrintAllocationCallTree(
            results.AllocationCallTree,
            callTreeRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeSiblingCutoffPercent);
    }
}

void PrintExceptionResults(
    ExceptionProfileResult? results,
    string profileName,
    string? description,
    string? rootFilter,
    string? exceptionTypeFilter,
    string? functionFilter,
    bool includeRuntime,
    int callTreeDepth,
    int callTreeWidth,
    string? callTreeRootMode,
    int callTreeSiblingCutoffPercent)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    PrintSection($"EXCEPTION PROFILE: {profileName}");
    if (!string.IsNullOrWhiteSpace(description))
    {
        AnsiConsole.MarkupLine($"[dim]{description}[/]");
    }

    var selectedType = SelectExceptionType(results.ExceptionTypes, exceptionTypeFilter);
    var filteredExceptionTypes = FilterExceptionTypes(results.ExceptionTypes, exceptionTypeFilter);
    var hasTypeFilter = !string.IsNullOrWhiteSpace(exceptionTypeFilter);
    ExceptionTypeDetails? selectedDetails = null;
    if (!string.IsNullOrWhiteSpace(selectedType) &&
        results.TypeDetails.TryGetValue(selectedType, out var details))
    {
        selectedDetails = details;
    }

    if (hasTypeFilter && filteredExceptionTypes.Count == 0)
    {
        AnsiConsole.MarkupLine(
            $"[yellow]No exception types matched '{Markup.Escape(exceptionTypeFilter!)}'. Showing full results.[/]");
        filteredExceptionTypes = results.ExceptionTypes;
        selectedType = null;
        selectedDetails = null;
    }
    else if (hasTypeFilter && !string.IsNullOrWhiteSpace(selectedType))
    {
        AnsiConsole.MarkupLine($"[dim]Exception type filter: {Markup.Escape(selectedType)}[/]");
    }

    if (filteredExceptionTypes.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No exception events captured.[/]");
    }
    else
    {
        AnsiConsole.WriteLine();
        var table = new Table()
            .Border(TableBorder.None)
            .Title("[bold]Top Exceptions (Thrown)[/]")
            .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Exception[/]"));

        foreach (var entry in filteredExceptionTypes.Take(15))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Type);
            if (typeName.Length > 70)
            {
                typeName = typeName[..67] + "...";
            }

            var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
            table.AddRow($"[blue]{countText}[/]", Markup.Escape(typeName));
        }

        AnsiConsole.Write(table);
    }

    var summaryThrown = selectedDetails?.Thrown ?? results.TotalThrown;
    var summaryCaught = selectedDetails?.Caught ?? results.TotalCaught;
    var summaryTable = new Table()
        .Border(TableBorder.None)
        .Title("[bold yellow]Summary[/]")
        .HideHeaders()
        .AddColumn("")
        .AddColumn("");

    var thrownText = summaryThrown.ToString("N0", CultureInfo.InvariantCulture);
    summaryTable.AddRow("[bold]Thrown[/]", $"[green]{thrownText}[/]");
    if (summaryCaught > 0)
    {
        var caughtText = summaryCaught.ToString("N0", CultureInfo.InvariantCulture);
        summaryTable.AddRow("[bold]Caught[/]", $"[blue]{caughtText}[/]");
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(summaryTable);

    if (summaryThrown > 0)
    {
        var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
        AnsiConsole.Write(BuildExceptionCallTree(
            selectedDetails?.ThrowRoot ?? results.ThrowCallTreeRoot,
            summaryThrown,
            "Call Tree (Thrown Exceptions)",
            selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
            resolvedRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoffPercent));
    }

    var catchSites = selectedDetails?.CatchSites ?? results.CatchSites;
    var catchRoot = selectedDetails?.CatchRoot ?? results.CatchCallTreeRoot;
    if (summaryCaught > 0 && catchRoot != null)
    {
        var filteredCatchSites = catchSites.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
        if (!includeRuntime)
        {
            filteredCatchSites = filteredCatchSites.Where(entry => !IsRuntimeNoise(entry.Name));
        }
        var catchList = filteredCatchSites.ToList();
        if (catchList.Count > 0)
        {
            AnsiConsole.WriteLine();
            var catchTable = new Table()
                .Border(TableBorder.None)
                .Title("[bold]Top Catch Sites[/]")
                .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
                .AddColumn(new TableColumn("[yellow]Function[/]"));

            foreach (var entry in catchList.Take(15))
            {
                var funcName = NameFormatter.FormatMethodDisplayName(entry.Name);
                if (funcName.Length > 70)
                {
                    funcName = funcName[..67] + "...";
                }

                var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
                catchTable.AddRow($"[blue]{countText}[/]", Markup.Escape(funcName));
            }

            AnsiConsole.Write(catchTable);
        }

        var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
        AnsiConsole.Write(BuildExceptionCallTree(
            catchRoot,
            summaryCaught,
            "Call Tree (Catch Sites)",
            selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
            resolvedRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoffPercent));
    }
}

void PrintContentionResults(
    ContentionProfileResult? results,
    string profileName,
    string? description,
    string? rootFilter,
    string? functionFilter,
    bool includeRuntime,
    int callTreeDepth,
    int callTreeWidth,
    string? callTreeRootMode,
    int callTreeSiblingCutoffPercent)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    PrintSection($"LOCK CONTENTION PROFILE: {profileName}");
    if (!string.IsNullOrWhiteSpace(description))
    {
        AnsiConsole.MarkupLine($"[dim]{description}[/]");
    }

    var filteredAll = results.TopFunctions.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
    if (!includeRuntime)
    {
        filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
    }
    var filteredList = filteredAll.ToList();

    AnsiConsole.WriteLine();
    var topTitle = includeRuntime && string.IsNullOrWhiteSpace(functionFilter)
        ? "Top Contended Functions (All)"
        : "Top Contended Functions (Filtered)";
    var table = new Table()
        .Border(TableBorder.None)
        .Title($"[bold]{topTitle}[/]")
        .AddColumn(new TableColumn("[yellow]Wait (ms)[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Function[/]"));

    foreach (var entry in filteredList.Take(15))
    {
        var funcName = NameFormatter.FormatMethodDisplayName(entry.Name);
        if (funcName.Length > 70)
        {
            funcName = funcName[..67] + "...";
        }

        var waitText = entry.TimeMs.ToString("F2", CultureInfo.InvariantCulture);
        var countText = entry.Calls.ToString("N0", CultureInfo.InvariantCulture);
        table.AddRow(
            $"[green]{waitText}[/]",
            $"[blue]{countText}[/]",
            Markup.Escape(funcName));
    }

    AnsiConsole.Write(table);
    var filteredOut = results.TopFunctions.Count - filteredList.Count;
    if (filteredOut > 0)
    {
        var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine(
            $"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
    }
    if (!string.IsNullOrWhiteSpace(functionFilter))
    {
        AnsiConsole.MarkupLine(
            $"[dim]Filter: {Markup.Escape(functionFilter)} (use --filter to change).[/]");
    }

    AnsiConsole.WriteLine();
    var summaryTable = new Table()
        .Border(TableBorder.None)
        .Title("[bold yellow]Summary[/]")
        .HideHeaders()
        .AddColumn("")
        .AddColumn("");

    var totalWaitText = results.TotalWaitMs.ToString("F2", CultureInfo.InvariantCulture);
    var totalCountText = results.TotalCount.ToString("N0", CultureInfo.InvariantCulture);
    summaryTable.AddRow("[bold]Total Wait[/]", $"[green]{totalWaitText} ms[/]");
    summaryTable.AddRow("[bold]Total Contentions[/]", $"[blue]{totalCountText}[/]");
    AnsiConsole.Write(summaryTable);

    var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
    AnsiConsole.Write(BuildContentionCallTree(
        results,
        resolvedRoot,
        includeRuntime,
        callTreeDepth,
        callTreeWidth,
        callTreeRootMode,
        callTreeSiblingCutoffPercent));
}

void PrintAllocationCallTree(
    AllocationCallTreeResult results,
    string? rootFilter,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    maxDepth = Math.Max(1, maxDepth);
    maxWidth = Math.Max(1, maxWidth);
    siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

    var roots = FilterAllocationRoots(results.TypeRoots, rootFilter);
    var visibleRoots = GetVisibleAllocationRoots(roots, maxWidth, siblingCutoffPercent);
    var totalBytes = results.TotalBytes;

    if (visibleRoots.Count == 0)
    {
        AnsiConsole.MarkupLine("[dim]No allocation call stacks captured.[/]");
        return;
    }

    foreach (var root in visibleRoots)
    {
        var pct = totalBytes > 0 ? 100d * root.TotalBytes / totalBytes : 0d;
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var bytesText = FormatBytes(root.TotalBytes);
        var countText = root.Count.ToString("N0", CultureInfo.InvariantCulture);
        var header = $"{NameFormatter.FormatTypeDisplayName(root.Name)} ({bytesText}, {pctText}%, {countText}x)";

        var tree = BuildAllocationCallTree(root, includeRuntime, maxDepth, maxWidth, siblingCutoffPercent);
        AnsiConsole.Write(new Rows(new Markup($"[bold yellow]{Markup.Escape(header)}[/]"), tree));
    }
}

IEnumerable<AllocationCallTreeNode> FilterAllocationRoots(
    IReadOnlyList<AllocationCallTreeNode> roots,
    string? rootFilter)
{
    if (string.IsNullOrWhiteSpace(rootFilter))
    {
        return roots;
    }

    var filter = rootFilter.Trim();
    return roots.Where(root =>
        root.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        NameFormatter.FormatTypeDisplayName(root.Name).Contains(filter, StringComparison.OrdinalIgnoreCase));
}

IReadOnlyList<AllocationCallTreeNode> GetVisibleAllocationRoots(
    IEnumerable<AllocationCallTreeNode> roots,
    int maxWidth,
    int siblingCutoffPercent)
{
    var ordered = roots
        .OrderByDescending(root => root.TotalBytes)
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
        .Where(root => root.TotalBytes >= minBytes)
        .Take(maxWidth)
        .ToList();
}

IRenderable BuildContentionCallTree(
    ContentionProfileResult results,
    string? rootFilter,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    string? rootMode,
    int siblingCutoffPercent)
{
    var callTreeRoot = results.CallTreeRoot;
    var totalTime = results.TotalWaitMs;
    var title = "Call Tree (Wait Time)";
    maxDepth = Math.Max(1, maxDepth);
    maxWidth = Math.Max(1, maxWidth);
    siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

    var rootNode = callTreeRoot;
    var rootTotal = totalTime;
    if (!string.IsNullOrWhiteSpace(rootFilter))
    {
        var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
        if (matches.Count > 0)
        {
            rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
            rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
            title = $"{title} - root: {Markup.Escape(rootFilter)}";
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
        }
    }

    var rootLabel = FormatCallTreeLine(rootNode, rootTotal, useSelfTime: false, isRoot: true);
    var tree = new Tree(rootLabel)
    {
        Style = new Style(Color.Grey),
        Guide = new CompactTreeGuide()
    };
    var children = CallTreeFilters.GetVisibleChildren(
        rootNode,
        includeRuntime,
        useSelfTime: false,
        maxWidth,
        siblingCutoffPercent,
        IsRuntimeNoise);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                     CallTreeFilters.GetVisibleChildren(
                         child,
                         includeRuntime,
                         useSelfTime: false,
                         maxWidth,
                         siblingCutoffPercent,
                         IsRuntimeNoise).Count == 0;
        var childNode = tree.AddNode(FormatCallTreeLine(child, rootTotal, useSelfTime: false, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddCallTreeChildren(
                childNode,
                child,
                rootTotal,
                useSelfTime: false,
                includeRuntime,
                2,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }

    return new Rows(
        new Markup($"[bold yellow]{title}[/]"),
        tree);
}

IRenderable BuildExceptionCallTree(
    CallTreeNode callTreeRoot,
    long totalCount,
    string title,
    string? rootLabelOverride,
    string? rootFilter,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    string? rootMode,
    int siblingCutoffPercent)
{
    maxDepth = Math.Max(1, maxDepth);
    maxWidth = Math.Max(1, maxWidth);
    siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

    var rootNode = callTreeRoot;
    var rootTotal = (double)totalCount;
    if (!string.IsNullOrWhiteSpace(rootFilter))
    {
        var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
        if (matches.Count > 0)
        {
            rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
            rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
            title = $"{title} - root: {Markup.Escape(rootFilter)}";
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
        }
    }

    var rootLabel = FormatExceptionCallTreeLine(rootNode, rootTotal, isRoot: true, rootLabelOverride);
    var tree = new Tree(rootLabel)
    {
        Style = new Style(Color.Grey),
        Guide = new CompactTreeGuide()
    };
    var children = CallTreeFilters.GetVisibleChildren(
        rootNode,
        includeRuntime,
        useSelfTime: false,
        maxWidth,
        siblingCutoffPercent,
        IsRuntimeNoise);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                     CallTreeFilters.GetVisibleChildren(
                         child,
                         includeRuntime,
                         useSelfTime: false,
                         maxWidth,
                         siblingCutoffPercent,
                         IsRuntimeNoise).Count == 0;
        var childNode = tree.AddNode(FormatExceptionCallTreeLine(child, rootTotal, isRoot: false, rootLabelOverride: null, isLeaf));
        if (!isSpecialLeaf)
        {
            AddExceptionCallTreeChildren(
                childNode,
                child,
                rootTotal,
                includeRuntime,
                2,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }

    return new Rows(
        new Markup($"[bold yellow]{title}[/]"),
        tree);
}

IRenderable BuildAllocationCallTree(
    AllocationCallTreeNode root,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    int siblingCutoffPercent)
{
    var rootLabel = FormatAllocationCallTreeLine(root, root.TotalBytes, isRoot: true, isLeaf: false);
    var tree = new Tree(rootLabel)
    {
        Style = new Style(Color.Grey),
        Guide = new CompactTreeGuide()
    };
    var children = GetVisibleAllocationChildren(root, includeRuntime, maxWidth, siblingCutoffPercent);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(NameFormatter.FormatMethodDisplayName(child.Name));
        var childChildren = !isSpecialLeaf
            ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
            : Array.Empty<AllocationCallTreeNode>();
        var isLeaf = isSpecialLeaf || maxDepth <= 1 || childChildren.Count == 0;

        var childNode = tree.AddNode(FormatAllocationCallTreeLine(child, root.TotalBytes, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddAllocationCallTreeChildren(
                childNode,
                child,
                root.TotalBytes,
                includeRuntime,
                2,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }

    return tree;
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
        var isSpecialLeaf = ShouldStopAtLeaf(NameFormatter.FormatMethodDisplayName(child.Name));
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

    var displayName = isRoot ? NameFormatter.FormatTypeDisplayName(node.Name) : NameFormatter.FormatMethodDisplayName(node.Name);
    if (displayName.Length > 80)
    {
        displayName = displayName[..77] + "...";
    }

    var nameText = isRoot
        ? $"[white]{Markup.Escape(displayName)}[/]"
        : FormatCallTreeName(displayName, displayName, isLeaf);

    return $"[green]{bytesText}[/] [cyan]{pctText}%[/] [blue]{countText}x[/] {nameText}";
}

IRenderable BuildCallTree(
    CpuProfileResult results,
    bool useSelfTime,
    string? rootFilter,
    bool includeRuntime,
    int maxDepth,
    int maxWidth,
    string? rootMode,
    int siblingCutoffPercent)
{
    var callTreeRoot = results.CallTreeRoot;
    var totalTime = results.CallTreeTotal;
    var title = useSelfTime ? "Call Tree (Self Time)" : "Call Tree (Total Time)";
    maxDepth = Math.Max(1, maxDepth);
    maxWidth = Math.Max(1, maxWidth);
    siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

    var rootNode = callTreeRoot;
    var rootTotal = totalTime;
    if (!string.IsNullOrWhiteSpace(rootFilter))
    {
        var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
        if (matches.Count > 0)
        {
            rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
            rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
            title = $"{title} - root: {Markup.Escape(rootFilter)}";
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
        }
    }

    var rootLabel = FormatCallTreeLine(rootNode, rootTotal, useSelfTime, isRoot: true);
    var tree = new Tree(rootLabel)
    {
        Style = new Style(Color.Grey),
        Guide = new CompactTreeGuide()
    };
    var children = CallTreeFilters.GetVisibleChildren(
        rootNode,
        includeRuntime,
        useSelfTime,
        maxWidth,
        siblingCutoffPercent,
        IsRuntimeNoise);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                     CallTreeFilters.GetVisibleChildren(
                         child,
                         includeRuntime,
                         useSelfTime,
                         maxWidth,
                         siblingCutoffPercent,
                         IsRuntimeNoise).Count == 0;
        var childNode = tree.AddNode(FormatCallTreeLine(child, rootTotal, useSelfTime, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddCallTreeChildren(
                childNode,
                child,
                rootTotal,
                useSelfTime,
                includeRuntime,
                2,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }

    return new Rows(
        new Markup($"[bold yellow]{title}[/]"),
        tree);
}

void AddCallTreeChildren(
    TreeNode parent,
    CallTreeNode node,
    double totalTime,
    bool useSelfTime,
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

    var children = CallTreeFilters.GetVisibleChildren(
        node,
        includeRuntime,
        useSelfTime,
        maxWidth,
        siblingCutoffPercent,
        IsRuntimeNoise);

    foreach (var child in children)
    {
        var nextDepth = depth + 1;
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
            ? CallTreeFilters.GetVisibleChildren(
                child,
                includeRuntime,
                useSelfTime,
                maxWidth,
                siblingCutoffPercent,
                IsRuntimeNoise)
            : Array.Empty<CallTreeNode>();
        var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

        var childNode = parent.AddNode(FormatCallTreeLine(child, totalTime, useSelfTime, isRoot: false, isLeaf));
        if (!isSpecialLeaf)
        {
            AddCallTreeChildren(
                childNode,
                child,
                totalTime,
                useSelfTime,
                includeRuntime,
                depth + 1,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }
}

void AddExceptionCallTreeChildren(
    TreeNode parent,
    CallTreeNode node,
    double totalCount,
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

    var children = CallTreeFilters.GetVisibleChildren(
        node,
        includeRuntime,
        useSelfTime: false,
        maxWidth,
        siblingCutoffPercent,
        IsRuntimeNoise);
    foreach (var child in children)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || depth + 1 > maxDepth ||
                     CallTreeFilters.GetVisibleChildren(
                         child,
                         includeRuntime,
                         useSelfTime: false,
                         maxWidth,
                         siblingCutoffPercent,
                         IsRuntimeNoise).Count == 0;
        var childNode = parent.AddNode(FormatExceptionCallTreeLine(child, totalCount, isRoot: false, rootLabelOverride: null, isLeaf));
        if (!isSpecialLeaf)
        {
            AddExceptionCallTreeChildren(
                childNode,
                child,
                totalCount,
                includeRuntime,
                depth + 1,
                maxDepth,
                maxWidth,
                siblingCutoffPercent);
        }
    }
}

string FormatCallTreeLine(
    CallTreeNode node,
    double totalTime,
    bool useSelfTime,
    bool isRoot,
    bool isLeaf = false)
{
    var matchName = GetCallTreeMatchName(node);
    var displayName = matchName;
    if (displayName.Length > 80)
    {
        displayName = displayName[..77] + "...";
    }

    var timeSpent = isRoot && useSelfTime
        ? GetCallTreeTime(node, useSelfTime: false)
        : GetCallTreeTime(node, useSelfTime);
    var calls = node.Calls;

    var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
    var timeText = timeSpent.ToString("F2", CultureInfo.InvariantCulture);
    var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
    var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
    var nameText = FormatCallTreeName(displayName, matchName, isLeaf);

    return $"[green]{timeText} ms[/] [cyan]{pctText}%[/] [blue]{callsText}x[/] {nameText}";
}

string FormatExceptionCallTreeLine(
    CallTreeNode node,
    double totalCount,
    bool isRoot,
    string? rootLabelOverride,
    bool isLeaf = false)
{
    var matchName = GetCallTreeMatchName(node);
    var displayName = isRoot && !string.IsNullOrWhiteSpace(rootLabelOverride)
        ? rootLabelOverride
        : matchName;
    if (displayName.Length > 80)
    {
        displayName = displayName[..77] + "...";
    }

    var count = isRoot ? totalCount : node.Total;
    var pct = totalCount > 0 ? 100 * count / totalCount : 0;
    var countText = count.ToString("N0", CultureInfo.InvariantCulture);
    var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
    var nameText = FormatCallTreeName(displayName, matchName, isLeaf);

    return $"[green]{countText}x[/] [cyan]{pctText}%[/] {nameText}";
}

CallTreeNode GetOrCreateCallTreeChild(
    CallTreeNode parent,
    int frameIdx,
    IReadOnlyList<string> frames)
{
    if (!parent.Children.TryGetValue(frameIdx, out var child))
    {
        var name = frameIdx >= 0 && frameIdx < frames.Count ? frames[frameIdx] : "Unknown";
        child = new CallTreeNode(frameIdx, name);
        parent.Children[frameIdx] = child;
    }

    return child;
}

double GetCallTreeTime(CallTreeNode node, bool useSelfTime)
{
    return useSelfTime ? node.Self : node.Total;
}

double SumCallTreeTotals(CallTreeNode node)
{
    var sum = 0d;
    foreach (var child in node.Children.Values)
    {
        sum += child.Total;
    }
    return sum;
}

int SumCallTreeCalls(CallTreeNode node)
{
    var sum = 0;
    foreach (var child in node.Children.Values)
    {
        sum += child.Calls;
    }
    return sum;
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
            var displayName = NameFormatter.FormatMethodDisplayName(current.Name);
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

string? ResolveCallTreeRootFilter(string? rootFilter)
{
    return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
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

void PrintAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
{
    if (entries.Count == 0)
    {
        return;
    }

    var table = new Table()
        .Border(TableBorder.None)
        .AddColumn(new TableColumn("[yellow]Type[/]"))
        .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
        .AddColumn(new TableColumn("[yellow]Total[/]").RightAligned());

    long totalCount = 0;

    foreach (var entry in entries)
    {
        var typeName = NameFormatter.FormatTypeDisplayName(entry.Type);
        if (typeName.Length > 80)
        {
            typeName = typeName[..77] + "...";
        }
        var count = entry.Count;
        var totalText = entry.Total ?? string.Empty;

        totalCount += count;

        var countText = count.ToString("N0", CultureInfo.InvariantCulture);
        table.AddRow(
            $"[white]{Markup.Escape(typeName)}[/]",
            $"[blue]{Markup.Escape(countText)}[/]",
            $"[green]{Markup.Escape(totalText)}[/]");
    }

    if (!string.IsNullOrWhiteSpace(allocationTotal))
    {
        var countText = totalCount.ToString("N0", CultureInfo.InvariantCulture);
        table.AddRow(
            "[bold white]TOTAL (shown)[/]",
            $"[bold blue]{Markup.Escape(countText)}[/]",
            $"[bold green]{Markup.Escape(allocationTotal)}[/]");
    }

    AnsiConsole.Write(table);
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

string FormatCallTreeName(string displayName, string matchName, bool isLeaf)
{
    var escaped = Markup.Escape(displayName);
    if (!isLeaf)
    {
        return $"[white]{escaped}[/]";
    }

    const string leafHighlightColor = "plum1";
    if (matchName.Contains("CastHelpers.", StringComparison.Ordinal))
    {
        return $"[{leafHighlightColor}]{escaped}[/]";
    }

    if (matchName.Contains("Array.Copy", StringComparison.Ordinal) ||
        matchName.Contains("Dictionary<__Canon,__Canon>.Resize", StringComparison.Ordinal) ||
        matchName.Contains("Buffer.BulkMoveWithWriteBarrier", StringComparison.Ordinal) ||
        matchName.Contains("SpanHelpers.SequenceEqual", StringComparison.Ordinal) ||
        matchName.Contains("HashSet<", StringComparison.Ordinal) ||
        matchName.Contains("Enumerable+ArrayWhereSelectIterator<", StringComparison.Ordinal) ||
        matchName.Contains("ImmutableDictionary<", StringComparison.Ordinal) ||
        matchName.Contains("SegmentedArrayBuilder<__Canon>.ToArray", StringComparison.Ordinal) ||
        matchName.Contains("__Canon", StringComparison.Ordinal))
    {
        return $"[{leafHighlightColor}]{escaped}[/]";
    }

    if (matchName.Contains("List<", StringComparison.Ordinal) &&
        matchName.EndsWith(".ToArray", StringComparison.Ordinal))
    {
        return $"[{leafHighlightColor}]{escaped}[/]";
    }

    return $"[white]{escaped}[/]";
}

string GetCallTreeMatchName(CallTreeNode node)
{
    return NameFormatter.FormatMethodDisplayName(node.Name);
}

bool ShouldStopAtLeaf(string matchName)
{
    return matchName.Contains("CastHelpers.", StringComparison.Ordinal) ||
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

bool MatchesFunctionFilter(string name, string? filter)
{
    if (string.IsNullOrWhiteSpace(filter))
    {
        return true;
    }

    return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
           NameFormatter.FormatMethodDisplayName(name).Contains(filter, StringComparison.OrdinalIgnoreCase);
}

IReadOnlyList<ExceptionTypeSample> FilterExceptionTypes(
    IReadOnlyList<ExceptionTypeSample> types,
    string? filter)
{
    if (string.IsNullOrWhiteSpace(filter))
    {
        return types;
    }

    return types
        .Where(entry => entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        NameFormatter.FormatTypeDisplayName(entry.Type)
                            .Contains(filter, StringComparison.OrdinalIgnoreCase))
        .ToList();
}

string? SelectExceptionType(IReadOnlyList<ExceptionTypeSample> types, string? filter)
{
    if (string.IsNullOrWhiteSpace(filter))
    {
        return null;
    }

    foreach (var entry in types)
    {
        if (entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            NameFormatter.FormatTypeDisplayName(entry.Type)
                .Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return entry.Type;
        }
    }

    return null;
}

bool IsRuntimeNoise(string name)
{
    var trimmed = name.TrimStart();
    var formatted = NameFormatter.FormatMethodDisplayName(trimmed);
    return trimmed.Contains("UNMANAGED_CODE_TIME", StringComparison.Ordinal) ||
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
        AnsiConsole.MarkupLine("[red]No command provided for heap snapshot.[/]");
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
        AnsiConsole.MarkupLine("[red]Failed to start process for heap snapshot.[/]");
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
        AnsiConsole.MarkupLine($"[red]GC dump collection failed:[/] {Markup.Escape(stderr)}");
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

    AnsiConsole.MarkupLine($"[yellow]Could not parse gcdump, showing raw output:[/] {Markup.Escape(reportErr)}");
    return new HeapProfileResult(reportOut, Array.Empty<HeapTypeEntry>());
}

HeapProfileResult? HeapProfileFromInput(string inputPath)
{
    if (!File.Exists(inputPath))
    {
        AnsiConsole.MarkupLine($"[red]Input file not found:[/] {Markup.Escape(inputPath)}");
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

        AnsiConsole.MarkupLine($"[yellow]Could not parse gcdump, showing raw output:[/] {Markup.Escape(reportErr)}");
        return new HeapProfileResult(reportOut, Array.Empty<HeapTypeEntry>());
    }

    if (extension == ".txt" || extension == ".log")
    {
        var report = File.ReadAllText(inputPath);
        return ParseGcdumpReport(report);
    }

    AnsiConsole.MarkupLine($"[red]Unsupported heap input:[/] {Markup.Escape(inputPath)}");
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

void PrintHeapResults(HeapProfileResult? results, string profileName, string? description)
{
    if (results == null)
    {
        AnsiConsole.MarkupLine("[red]No results to display[/]");
        return;
    }

    PrintSection($"HEAP SNAPSHOT: {profileName}");
    if (!string.IsNullOrWhiteSpace(description))
    {
        AnsiConsole.MarkupLine($"[dim]{description}[/]");
    }

    if (results.Types.Count > 0)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn(new TableColumn("[yellow]Size (bytes)[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[yellow]Type[/]"));

        foreach (var entry in results.Types.Take(40))
        {
            var sizeText = entry.Size.ToString("N0", CultureInfo.InvariantCulture);
            var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
            var typeName = entry.Type.Length > 60 ? entry.Type[..57] + "..." : entry.Type;
            table.AddRow(sizeText, countText, Markup.Escape(typeName));
        }

        AnsiConsole.Write(table);
    }
    else if (!string.IsNullOrWhiteSpace(results.RawOutput))
    {
        AnsiConsole.WriteLine(results.RawOutput);
    }
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
var timelineOption = new Option<bool>("--timeline", "Show CPU profile as visual timeline (use with --cpu)");
var timelineWidthOption = new Option<int>("--timeline-width", () => 60, "Timeline bar width in characters (default: 60)");
var timelineMaxSpansOption = new Option<int>("--timeline-max-spans", () => 100, "Maximum spans to display (default: 100)");
var timelineMinDurationOption = new Option<double>("--timeline-min-duration", () => 0.1, "Minimum span duration in ms (default: 0.1)");
var memoryOption = new Option<bool>("--memory", "Run memory profiling only");
var exceptionOption = new Option<bool>("--exception", "Run exception profiling only");
exceptionOption.AddAlias("--exceptions");
var contentionOption = new Option<bool>("--contention", "Run lock contention profiling only");
var heapOption = new Option<bool>("--heap", "Capture heap snapshot");
var callTreeRootOption = new Option<string?>("--root", "Filter call tree to a root method (substring match)");
var callTreeDepthOption = new Option<int>("--calltree-depth", () => 30, "Maximum call tree depth (default: 30)");
var callTreeWidthOption = new Option<int>("--calltree-width", () => 4, "Maximum children per node (default: 4)");
var callTreeRootModeOption = new Option<string?>("--root-mode", () => "hottest", "Root selection mode when multiple matches (hottest|shallowest|first)");
var callTreeSelfOption = new Option<bool>("--calltree-self", "Show self-time call tree in addition to total time");
var callTreeSiblingCutoffOption = new Option<int>("--calltree-sibling-cutoff", () => 5, "Hide siblings below X% of the top sibling (default: 5)");
var functionFilterOption = new Option<string?>("--filter", "Filter CPU function tables by substring (case-insensitive)");
var exceptionTypeOption = new Option<string?>("--exception-type", "Filter exception tables and call trees by exception type (substring match)");
var includeRuntimeOption = new Option<bool>("--include-runtime", "Include runtime/process frames in CPU tables and call tree");
var inputOption = new Option<string?>("--input", "Render results from an existing trace file");
var targetFrameworkOption = new Option<string?>("--tfm", "Target framework to use for .csproj/.sln inputs (e.g. net8.0)");
var commandArg = new Argument<string[]>("command", () => Array.Empty<string>(),
    "Command to profile (pass after --)");
commandArg.Arity = ArgumentArity.ZeroOrMore;

var rootCommand = new RootCommand("Asynkron Profiler - CPU/Memory/Exception/Contention/Heap profiling for .NET commands")
{
    cpuOption,
    timelineOption,
    timelineWidthOption,
    timelineMaxSpansOption,
    timelineMinDurationOption,
    memoryOption,
    exceptionOption,
    contentionOption,
    heapOption,
    callTreeRootOption,
    callTreeDepthOption,
    callTreeWidthOption,
    callTreeRootModeOption,
    callTreeSelfOption,
    callTreeSiblingCutoffOption,
    functionFilterOption,
    exceptionTypeOption,
    includeRuntimeOption,
    inputOption,
    targetFrameworkOption,
    commandArg
};

rootCommand.TreatUnmatchedTokensAsErrors = false;

rootCommand.SetHandler(context =>
{
    var cpu = context.ParseResult.GetValueForOption(cpuOption);
    var timeline = context.ParseResult.GetValueForOption(timelineOption);
    var timelineWidth = context.ParseResult.GetValueForOption(timelineWidthOption);
    var timelineMaxSpans = context.ParseResult.GetValueForOption(timelineMaxSpansOption);
    var timelineMinDuration = context.ParseResult.GetValueForOption(timelineMinDurationOption);
    var memory = context.ParseResult.GetValueForOption(memoryOption);
    var exception = context.ParseResult.GetValueForOption(exceptionOption);
    var contention = context.ParseResult.GetValueForOption(contentionOption);
    var heap = context.ParseResult.GetValueForOption(heapOption);
    var callTreeRoot = context.ParseResult.GetValueForOption(callTreeRootOption);
    var callTreeDepth = context.ParseResult.GetValueForOption(callTreeDepthOption);
    var callTreeWidth = context.ParseResult.GetValueForOption(callTreeWidthOption);
    var callTreeRootMode = context.ParseResult.GetValueForOption(callTreeRootModeOption);
    var callTreeSelf = context.ParseResult.GetValueForOption(callTreeSelfOption);
    var callTreeSiblingCutoff = context.ParseResult.GetValueForOption(callTreeSiblingCutoffOption);
    var functionFilter = context.ParseResult.GetValueForOption(functionFilterOption);
    var exceptionTypeFilter = context.ParseResult.GetValueForOption(exceptionTypeOption);
    var includeRuntime = context.ParseResult.GetValueForOption(includeRuntimeOption);
    var inputPath = context.ParseResult.GetValueForOption(inputOption);
    var targetFramework = context.ParseResult.GetValueForOption(targetFrameworkOption);
    var command = context.ParseResult.GetValueForArgument(commandArg) ?? Array.Empty<string>();

    var hasInput = !string.IsNullOrWhiteSpace(inputPath);
    var hasExplicitModes = cpu || memory || heap || contention || exception;
    var runCpu = cpu || !hasExplicitModes;
    var runMemory = memory || !hasExplicitModes;
    var runHeap = heap;
    var runException = exception;
    var runContention = contention;

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
            AnsiConsole.MarkupLine("[red]No command provided.[/]");
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

    if (runCpu)
    {
        Console.WriteLine($"{label} - cpu");
        var results = hasInput
            ? CpuProfileFromInput(inputPath!, label)
            : CpuProfileCommand(command, label);
        PrintCpuResults(
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
            timeline,
            timelineWidth,
            timelineMaxSpans,
            timelineMinDuration);
    }

    if (runMemory)
    {
        Console.WriteLine($"{label} - memory");
        var results = hasInput
            ? MemoryProfileFromInput(inputPath!, label)
            : MemoryProfileCommand(command, label);
        PrintMemoryResults(
            results,
            label,
            description,
            callTreeRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeSiblingCutoff);
    }

    if (runException)
    {
        Console.WriteLine($"{label} - exception");
        var results = hasInput
            ? ExceptionProfileFromInput(inputPath!, label)
            : ExceptionProfileCommand(command, label);
        PrintExceptionResults(
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
        PrintContentionResults(
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
        PrintHeapResults(results, label, description);
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
