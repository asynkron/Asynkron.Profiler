using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfileInputLoader
{
    private readonly ProfilerTraceAnalyzer _traceAnalyzer;
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, string, bool> _ensureToolAvailable;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Func<string, HeapProfileResult> _parseGcdumpReport;
    private readonly Action<string> _writeLine;
    private readonly string _dotnetGcdumpInstallHint;

    public ProfileInputLoader(
        ProfilerTraceAnalyzer traceAnalyzer,
        Func<Theme> getTheme,
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseGcdumpReport,
        Action<string> writeLine,
        string dotnetGcdumpInstallHint)
    {
        _traceAnalyzer = traceAnalyzer;
        _getTheme = getTheme;
        _ensureToolAvailable = ensureToolAvailable;
        _runProcess = runProcess;
        _parseGcdumpReport = parseGcdumpReport;
        _writeLine = writeLine;
        _dotnetGcdumpInstallHint = dotnetGcdumpInstallHint;
    }

    public CpuProfileResult? LoadCpu(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        var inputKind = ProfileInputClassifier.Classify(inputPath);
        if (inputKind == ProfileInputKind.SpeedscopeJson)
        {
            return AnalyzeSpeedscope(inputPath);
        }

        if (!ProfileInputClassifier.SupportsCpu(inputKind))
        {
            WriteUnsupportedInput("Unsupported CPU input", inputPath);
            return null;
        }

        return AnalyzeCpuTrace(inputPath);
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, ProfileInputClassifier.SupportsTraceAnalysis, "Unsupported memory input"))
        {
            return null;
        }

        var callTree = AnalyzeAllocationTrace(inputPath);
        return callTree == null ? null : BuildMemoryProfileResult(callTree);
    }

    public ExceptionProfileResult? LoadException(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, ProfileInputClassifier.SupportsTraceAnalysis, "Unsupported exception input"))
        {
            return null;
        }

        return AnalyzeExceptionTrace(inputPath);
    }

    public ContentionProfileResult? LoadContention(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, ProfileInputClassifier.SupportsTraceAnalysis, "Unsupported contention input"))
        {
            return null;
        }

        return AnalyzeContentionTrace(inputPath);
    }

    public HeapProfileResult? LoadHeap(string inputPath)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return null;
        }

        var inputKind = ProfileInputClassifier.Classify(inputPath);
        if (inputKind == ProfileInputKind.Gcdump)
        {
            if (!_ensureToolAvailable("dotnet-gcdump", _dotnetGcdumpInstallHint))
            {
                return null;
            }

            return GcdumpReportLoader.Load(
                inputPath,
                _getTheme(),
                _runProcess,
                _parseGcdumpReport,
                _writeLine);
        }

        if (inputKind == ProfileInputKind.HeapTextReport)
        {
            return _parseGcdumpReport(File.ReadAllText(inputPath));
        }

        WriteUnsupportedInput("Unsupported heap input", inputPath);
        return null;
    }

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        var result = TryAnalyzeTrace(
            () => _traceAnalyzer.AnalyzeCpuTrace(traceFile),
            "CPU trace parse failed");
        if (result == null)
        {
            return null;
        }

        if (result.AllFunctions.Count == 0)
        {
            _writeLine($"[{_getTheme().AccentColor}]No CPU samples found in trace.[/]");
            return null;
        }

        return result;
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        return TryAnalyzeTrace(
            () => _traceAnalyzer.AnalyzeAllocationTrace(traceFile),
            "Allocation trace parse failed");
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        return TryAnalyzeTrace(
            () => _traceAnalyzer.AnalyzeExceptionTrace(traceFile),
            "Exception trace parse failed");
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        return TryAnalyzeTrace(
            () => _traceAnalyzer.AnalyzeContentionTrace(traceFile),
            "Contention trace parse failed");
    }

    public static MemoryProfileResult BuildMemoryProfileResult(AllocationCallTreeResult callTree)
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

    public static string BuildInputLabel(string inputPath)
    {
        return FileLabelSanitizer.Sanitize(Path.GetFileNameWithoutExtension(inputPath), "input");
    }

    public static void ApplyInputDefaults(
        string inputPath,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        ProfileInputClassifier.ApplyDefaults(
            ProfileInputClassifier.Classify(inputPath),
            ref runCpu,
            ref runMemory,
            ref runHeap,
            ref runException,
            ref runContention);
    }

    private CpuProfileResult? AnalyzeSpeedscope(string speedscopePath)
    {
        try
        {
            return ProfilerTraceAnalyzer.AnalyzeSpeedscope(speedscopePath);
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]Speedscope parse failed:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private bool TryValidateTraceInput(
        string inputPath,
        Func<ProfileInputKind, bool> supportsInput,
        string unsupportedMessage)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return false;
        }

        if (supportsInput(ProfileInputClassifier.Classify(inputPath)))
        {
            return true;
        }

        WriteUnsupportedInput(unsupportedMessage, inputPath);
        return false;
    }

    private bool TryEnsureInputExists(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return true;
        }

        _writeLine($"[{_getTheme().ErrorColor}]Input file not found:[/] {Markup.Escape(inputPath)}");
        return false;
    }

    private void WriteUnsupportedInput(string message, string inputPath)
    {
        _writeLine($"[{_getTheme().ErrorColor}]{message}:[/] {Markup.Escape(inputPath)}");
    }

    private T? TryAnalyzeTrace<T>(Func<T> analysis, string errorPrefix)
    {
        try
        {
            return analysis();
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]{errorPrefix}:[/] {Markup.Escape(ex.Message)}");
            return default;
        }
    }
}
