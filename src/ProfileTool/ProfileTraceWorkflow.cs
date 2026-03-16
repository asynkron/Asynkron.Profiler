using System.Diagnostics;
using System.Globalization;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfileTraceWorkflow
{
    private const string DotnetTraceInstall = "dotnet tool install -g dotnet-trace";
    private const string DotnetGcdumpInstall = "dotnet tool install -g dotnet-gcdump";

    private static readonly string[] VersionProbeArgs = ["--version"];

    private readonly string _outputDir;
    private readonly Func<Theme> _getTheme;
    private readonly Func<JitCommandRunner> _getJitCommandRunner;
    private readonly Func<JitOutputFormatter> _getJitOutputFormatter;
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;
    private readonly Dictionary<string, bool> _toolAvailability = new(StringComparer.OrdinalIgnoreCase);

    public ProfileTraceWorkflow(
        string outputDir,
        Func<Theme> getTheme,
        Func<JitCommandRunner> getJitCommandRunner,
        Func<JitOutputFormatter> getJitOutputFormatter)
    {
        _outputDir = outputDir;
        _getTheme = getTheme;
        _getJitCommandRunner = getJitCommandRunner;
        _getJitOutputFormatter = getJitOutputFormatter;
        _traceAnalyzer = new ProfilerTraceAnalyzer(outputDir);
        InputLoader = new ProfileInputLoader(
            _traceAnalyzer,
            _getTheme,
            EnsureToolAvailable,
            ProcessRunner.Run,
            ParseGcdumpReport,
            AnsiConsole.MarkupLine,
            DotnetGcdumpInstall);
    }

    public ProfileInputLoader InputLoader { get; }

    public static bool TryParseHotThreshold(string? input, out double value)
    {
        value = ProfileCommandOptions.DefaultHotnessThreshold;
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

    public string? CollectCpuTrace(string[] command, string label, bool includeMemory, bool includeException)
    {
        if (!EnsureToolAvailable("dotnet-trace", DotnetTraceInstall))
        {
            return null;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var traceFile = Path.Combine(_outputDir, $"{label}_{timestamp}.nettrace");
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
        var theme = _getTheme();

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

    public CpuProfileResult? ProfileCpu(string[] command, string label)
    {
        return CollectTraceAndAnalyze(
            command,
            label,
            "nettrace",
            $"Running CPU profile on [{_getTheme().AccentColor}]{label}[/]...",
            "Trace collection failed",
            "Analyzing profile data...",
            collectArgs =>
            {
                collectArgs.Add("--providers");
                collectArgs.Add("Microsoft-DotNETCore-SampleProfiler");
            },
            InputLoader.AnalyzeCpuTrace);
    }

    public MemoryProfileResult? ProfileMemory(string[] command, string label)
    {
        var callTree = CollectTraceAndAnalyze(
            command,
            label,
            "alloc.nettrace",
            $"Collecting allocation trace for [{_getTheme().AccentColor}]{label}[/]...",
            "Allocation trace failed",
            "Analyzing allocation trace...",
            collectArgs =>
            {
                collectArgs.Add("--profile");
                collectArgs.Add("gc-verbose");
            },
            InputLoader.AnalyzeAllocationTrace);

        return callTree == null ? null : ProfileInputLoader.BuildMemoryProfileResult(callTree);
    }

    public ExceptionProfileResult? ProfileException(string[] command, string label)
    {
        var provider = BuildExceptionProvider();
        return CollectTraceAndAnalyze(
            command,
            label,
            "exc.nettrace",
            $"Collecting exception trace for [{_getTheme().AccentColor}]{label}[/]...",
            "Exception trace failed",
            "Analyzing exception trace...",
            collectArgs =>
            {
                collectArgs.Add("--providers");
                collectArgs.Add(provider);
            },
            InputLoader.AnalyzeExceptionTrace);
    }

    public ContentionProfileResult? ProfileContention(string[] command, string label)
    {
        var keywordsValue = ClrTraceEventParser.Keywords.Contention | ClrTraceEventParser.Keywords.Threading;
        var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
        var provider = $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
        return CollectTraceAndAnalyze(
            command,
            label,
            "cont.nettrace",
            $"Collecting lock contention trace for [{_getTheme().AccentColor}]{label}[/]...",
            "Contention trace failed",
            "Analyzing contention trace...",
            collectArgs =>
            {
                collectArgs.Add("--providers");
                collectArgs.Add(provider);
            },
            InputLoader.AnalyzeContentionTrace);
    }

    public HeapProfileResult? ProfileHeap(string[] command, string label)
    {
        if (!EnsureToolAvailable("dotnet-gcdump", DotnetGcdumpInstall))
        {
            return null;
        }

        var theme = _getTheme();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var gcdumpFile = Path.Combine(_outputDir, $"{label}_{timestamp}.gcdump");

        AnsiConsole.MarkupLine("[dim]Capturing heap snapshot...[/]");

        if (command.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided for heap snapshot.[/]");
            return null;
        }

        var psi = CommandProcessStartInfoFactory.Create(command);
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

    public void RunHotJitDisasm(
        CpuProfileResult results,
        string[] command,
        string? rootFilter,
        string? rootMode,
        bool includeRuntime,
        double hotThreshold)
    {
        var theme = _getTheme();
        var jitCommandRunner = _getJitCommandRunner();
        var jitOutputFormatter = _getJitOutputFormatter();
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

            ProfileFileListWriter.Write("JIT disasm files", theme.AccentColor, dumpFiles);

            if (hasMarkers && !string.IsNullOrWhiteSpace(logPath))
            {
                jitOutputFormatter.PrintDisasmSummary(logPath);
            }

            index++;
        }
    }

    private bool EnsureToolAvailable(string toolName, string installHint)
    {
        if (_toolAvailability.TryGetValue(toolName, out var cached))
        {
            return cached;
        }

        var theme = _getTheme();
        var (success, _, stderr) = ProcessRunner.Run(toolName, VersionProbeArgs, timeoutMs: 10000);
        if (!success)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "Tool not found." : stderr.Trim();
            AnsiConsole.MarkupLine($"[{theme.ErrorColor}]{toolName} unavailable:[/] {Markup.Escape(detail)}");
            AnsiConsole.MarkupLine($"[{theme.AccentColor}]Install:[/] {Markup.Escape(installHint)}");
            _toolAvailability[toolName] = false;
            return false;
        }

        _toolAvailability[toolName] = true;
        return true;
    }

    private string BuildExceptionProvider()
    {
        var keywordsValue = ClrTraceEventParser.Keywords.Exception;
        var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
        return $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
    }

    private TResult? CollectTraceAndAnalyze<TResult>(
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

        var theme = _getTheme();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var traceFile = Path.Combine(_outputDir, $"{label}_{timestamp}.{traceSuffix}");

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

    private static HeapProfileResult ParseGcdumpReport(string output)
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

    private static bool TryParseLong(string input, out long value)
    {
        return long.TryParse(
            input.Replace(",", "", StringComparison.Ordinal),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }
}
