using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfileSessionRunner
{
    private readonly string _outputDir;
    private readonly Func<Theme> _getTheme;
    private readonly ProfileInputLoader _profileInputLoader;
    private readonly Func<JitCommandRunner> _getJitCommandRunner;
    private readonly Func<JitOutputFormatter> _getJitOutputFormatter;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly Action<string> _writeMarkupLine;
    private readonly string _dotnetTraceInstall;
    private readonly string _dotnetGcdumpInstall;

    public ProfileSessionRunner(
        string outputDir,
        Func<Theme> getTheme,
        ProfileInputLoader profileInputLoader,
        Func<JitCommandRunner> getJitCommandRunner,
        Func<JitOutputFormatter> getJitOutputFormatter,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeMarkupLine,
        string dotnetTraceInstall,
        string dotnetGcdumpInstall)
    {
        _outputDir = outputDir;
        _getTheme = getTheme;
        _profileInputLoader = profileInputLoader;
        _getJitCommandRunner = getJitCommandRunner;
        _getJitOutputFormatter = getJitOutputFormatter;
        _ensureToolAvailable = ensureToolAvailable;
        _parseGcdumpReport = parseGcdumpReport;
        _writeMarkupLine = writeMarkupLine;
        _dotnetTraceInstall = dotnetTraceInstall;
        _dotnetGcdumpInstall = dotnetGcdumpInstall;
    }

    public string? CollectCpuTrace(string[] command, string label, bool includeMemory, bool includeException)
    {
        if (!_ensureToolAvailable("dotnet-trace", _dotnetTraceInstall))
        {
            return null;
        }

        var theme = _getTheme();
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

    public CpuProfileResult? CpuProfileCommand(string[] command, string label)
    {
        if (!_ensureToolAvailable("dotnet-trace", _dotnetTraceInstall))
        {
            return null;
        }

        var theme = _getTheme();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var traceFile = Path.Combine(_outputDir, $"{label}_{timestamp}.nettrace");

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
                return _profileInputLoader.AnalyzeCpuTrace(traceFile);
            });
    }

    public MemoryProfileResult? MemoryProfileCommand(string[] command, string label)
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
            _profileInputLoader.AnalyzeAllocationTrace);

        return callTree == null
            ? null
            : ProfileInputLoader.BuildMemoryProfileResult(callTree);
    }

    public ExceptionProfileResult? ExceptionProfileCommand(string[] command, string label)
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
            _profileInputLoader.AnalyzeExceptionTrace);
    }

    public ContentionProfileResult? ContentionProfileCommand(string[] command, string label)
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
            _profileInputLoader.AnalyzeContentionTrace);
    }

    public HeapProfileResult? HeapProfileCommand(string[] command, string label)
    {
        if (!_ensureToolAvailable("dotnet-gcdump", _dotnetGcdumpInstall))
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
            [
                "collect",
                "-p",
                proc.Id.ToString(CultureInfo.InvariantCulture),
                "-o",
                gcdumpFile
            ],
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
            _parseGcdumpReport,
            _writeMarkupLine);
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

        var jitCommandRunner = _getJitCommandRunner();
        var jitOutputFormatter = _getJitOutputFormatter();
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

            AnsiConsole.MarkupLine($"[{theme.AccentColor}]JIT disasm files:[/]");
            foreach (var file in dumpFiles)
            {
                Console.WriteLine(file);
            }

            if (hasMarkers && !string.IsNullOrWhiteSpace(logPath))
            {
                jitOutputFormatter.PrintDisasmSummary(logPath);
            }

            index++;
        }
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
        if (!_ensureToolAvailable("dotnet-trace", _dotnetTraceInstall))
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

    private static string BuildExceptionProvider()
    {
        var keywordsValue = ClrTraceEventParser.Keywords.Exception;
        var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
        return $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
    }
}
