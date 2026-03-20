using System;
using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfileCollectionRunner
{
    public const string DotnetTraceInstallHint = "dotnet tool install -g dotnet-trace";
    public const string DotnetGcdumpInstallHint = "dotnet tool install -g dotnet-gcdump";

    private readonly Func<Theme> _getTheme;
    private readonly ProfileCollectionServices _services;
    private readonly DotnetTraceCollector _traceCollector;
    private readonly HeapSnapshotCollector _heapSnapshotCollector;
    private readonly ProfileInputLoader _profileInputLoader;

    public ProfileCollectionRunner(
        string outputDirectory,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        ProfileInputLoader profileInputLoader,
        Action<string> writeLine)
    {
        _getTheme = getTheme;
        _services = new ProfileCollectionServices(outputDirectory, getTheme, ensureToolAvailable, runProcess, writeLine);
        _traceCollector = new DotnetTraceCollector(_services);
        _heapSnapshotCollector = new HeapSnapshotCollector(_services);
        _profileInputLoader = profileInputLoader;
    }

    public string? CollectCpuTrace(string[] command, string label, bool includeMemory, bool includeException)
    {
        var traceLabel = BuildSharedTraceLabel(includeMemory, includeException);
        var theme = _getTheme();

        return AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Collecting {traceLabel} trace for [{theme.AccentColor}]{label}[/]...", ctx =>
            {
                ctx.Status("Collecting trace data...");
                return _traceCollector.Collect(
                    command,
                    label,
                    "nettrace",
                    "Trace collection failed",
                    collectArgs =>
                    {
                        if (includeMemory)
                        {
                            collectArgs.Add("--profile");
                            collectArgs.Add("gc-verbose");
                        }

                        collectArgs.Add("--providers");
                        collectArgs.Add(DotnetTraceProviderFactory.BuildCpuProviderList(includeException));
                    });
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
                collectArgs.Add(DotnetTraceProviderFactory.BuildCpuProviderList(includeException: false));
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
                collectArgs.Add(DotnetTraceProviderFactory.BuildExceptionProvider());
            },
            _profileInputLoader.AnalyzeExceptionTrace);
    }

    public ContentionProfileResult? RunContentionProfile(string[] command, string label)
    {
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
                collectArgs.Add(DotnetTraceProviderFactory.BuildContentionProvider());
            },
            _profileInputLoader.AnalyzeContentionTrace);
    }

    public HeapProfileResult? RunHeapProfile(string[] command, string label)
    {
        AnsiConsole.MarkupLine("[dim]Capturing heap snapshot...[/]");
        var gcdumpFile = _heapSnapshotCollector.Collect(command, label);
        if (gcdumpFile == null)
        {
            return null;
        }

        return GcdumpReportLoader.Load(
            gcdumpFile,
            _getTheme(),
            _services.RunProcess,
            GcdumpReportParser.Parse,
            _services.WriteLine);
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
        return AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start(startMessage, ctx =>
            {
                ctx.Status("Collecting trace data...");
                var traceFile = _traceCollector.Collect(command, label, traceSuffix, failureLabel, configureCollectArgs);
                if (traceFile == null)
                {
                    return default;
                }

                ctx.Status(analysisStatus);
                return analyzeTrace(traceFile);
            });
    }

    private static string BuildSharedTraceLabel(bool includeMemory, bool includeException)
    {
        var traceParts = new List<string> { "CPU" };
        if (includeMemory)
        {
            traceParts.Add("allocation");
        }

        if (includeException)
        {
            traceParts.Add("exception");
        }

        return string.Join(" + ", traceParts);
    }
}
