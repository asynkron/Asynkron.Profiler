using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileCollectionRunner
{
    public const string DotnetTraceInstallHint = "dotnet tool install -g dotnet-trace";
    public const string DotnetGcdumpInstallHint = "dotnet tool install -g dotnet-gcdump";

    private readonly string _outputDirectory;
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly ProfileInputLoader _profileInputLoader;
    private readonly Action<string> _writeLine;

    public ProfileCollectionRunner(
        string outputDirectory,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        ProfileInputLoader profileInputLoader,
        Action<string> writeLine)
    {
        _outputDirectory = ArgumentGuard.RequireNotWhiteSpace(outputDirectory, nameof(outputDirectory), "Output directory is required.");
        _getTheme = getTheme;
        _ensureToolAvailable = ensureToolAvailable;
        _runProcess = runProcess;
        _profileInputLoader = profileInputLoader;
        _writeLine = writeLine;
    }

    public string? CollectCpuTrace(string[] command, string label, bool includeMemory, bool includeException)
    {
        if (!_ensureToolAvailable("dotnet-trace", DotnetTraceInstallHint))
        {
            return null;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var traceFile = Path.Combine(_outputDirectory, $"{label}_{timestamp}.nettrace");
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

                collectArgs.Add("--providers");
                collectArgs.Add(string.Join(",", providers));
                collectArgs.Add("--output");
                collectArgs.Add(traceFile);
                collectArgs.Add("--");
                collectArgs.AddRange(command);

                var (success, _, stderr) = _runProcess("dotnet-trace", collectArgs, null, 180000);
                if (!success || !File.Exists(traceFile))
                {
                    _writeLine($"[{_getTheme().ErrorColor}]Trace collection failed:[/] {Markup.Escape(stderr)}");
                    return null;
                }

                return traceFile;
            });
    }

    public CpuProfileResult? RunCpuProfile(string[] command, string label)
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
            _profileInputLoader.AnalyzeCpuTrace);
    }

    public MemoryProfileResult? RunMemoryProfile(string[] command, string label)
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

        return callTree == null ? null : ProfileInputLoader.BuildMemoryProfileResult(callTree);
    }

    public ExceptionProfileResult? RunExceptionProfile(string[] command, string label)
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

    public ContentionProfileResult? RunContentionProfile(string[] command, string label)
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

    public HeapProfileResult? RunHeapProfile(string[] command, string label)
    {
        if (!_ensureToolAvailable("dotnet-gcdump", DotnetGcdumpInstallHint))
        {
            return null;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var gcdumpFile = Path.Combine(_outputDirectory, $"{label}_{timestamp}.gcdump");
        var theme = _getTheme();

        AnsiConsole.MarkupLine("[dim]Capturing heap snapshot...[/]");

        if (command.Length == 0)
        {
            _writeLine($"[{theme.ErrorColor}]No command provided for heap snapshot.[/]");
            return null;
        }

        using var process = Process.Start(CommandStartInfoFactory.Create(command));
        if (process == null)
        {
            _writeLine($"[{theme.ErrorColor}]Failed to start process for heap snapshot.[/]");
            return null;
        }

        Thread.Sleep(500);

        var (success, _, stderr) = _runProcess(
            "dotnet-gcdump",
            ["collect", "-p", process.Id.ToString(CultureInfo.InvariantCulture), "-o", gcdumpFile],
            null,
            60000);

        process.WaitForExit();

        if (!success || !File.Exists(gcdumpFile))
        {
            _writeLine($"[{theme.ErrorColor}]GC dump collection failed:[/] {Markup.Escape(stderr)}");
            return null;
        }

        return GcdumpReportLoader.Load(
            gcdumpFile,
            theme,
            _runProcess,
            GcdumpReportParser.Parse,
            _writeLine);
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
        if (!_ensureToolAvailable("dotnet-trace", DotnetTraceInstallHint))
        {
            return default;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var traceFile = Path.Combine(_outputDirectory, $"{label}_{timestamp}.{traceSuffix}");

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

                var (success, _, stderr) = _runProcess("dotnet-trace", collectArgs, null, 180000);
                if (!success || !File.Exists(traceFile))
                {
                    _writeLine($"[{_getTheme().ErrorColor}]{failureLabel}:[/] {Markup.Escape(stderr)}");
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
