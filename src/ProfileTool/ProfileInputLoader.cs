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

        var extension = GetNormalizedExtension(inputPath);
        if (extension == ".json")
        {
            return AnalyzeSpeedscope(inputPath);
        }

        if (!IsSupportedExtension(extension, ".nettrace", ".etlx"))
        {
            WriteUnsupportedInput("Unsupported CPU input", inputPath);
            return null;
        }

        return AnalyzeCpuTrace(inputPath);
    }

    public MemoryProfileResult? LoadMemory(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported memory input"))
        {
            return null;
        }

        var callTree = AnalyzeAllocationTrace(inputPath);
        return callTree == null ? null : BuildMemoryProfileResult(callTree);
    }

    public ExceptionProfileResult? LoadException(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported exception input"))
        {
            return null;
        }

        return AnalyzeExceptionTrace(inputPath);
    }

    public ContentionProfileResult? LoadContention(string inputPath)
    {
        if (!TryValidateTraceInput(inputPath, "Unsupported contention input"))
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

        var extension = GetNormalizedExtension(inputPath);
        if (extension == ".gcdump")
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

        if (extension is ".txt" or ".log")
        {
            return _parseGcdumpReport(File.ReadAllText(inputPath));
        }

        WriteUnsupportedInput("Unsupported heap input", inputPath);
        return null;
    }

    public CpuProfileResult? AnalyzeCpuTrace(string traceFile)
    {
        try
        {
            var result = _traceAnalyzer.AnalyzeCpuTrace(traceFile);
            if (result.AllFunctions.Count == 0)
            {
                _writeLine($"[{_getTheme().AccentColor}]No CPU samples found in trace.[/]");
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]CPU trace parse failed:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    public AllocationCallTreeResult? AnalyzeAllocationTrace(string traceFile)
    {
        try
        {
            return _traceAnalyzer.AnalyzeAllocationTrace(traceFile);
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]Allocation trace parse failed:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    public ExceptionProfileResult? AnalyzeExceptionTrace(string traceFile)
    {
        try
        {
            return _traceAnalyzer.AnalyzeExceptionTrace(traceFile);
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]Exception trace parse failed:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    public ContentionProfileResult? AnalyzeContentionTrace(string traceFile)
    {
        try
        {
            return _traceAnalyzer.AnalyzeContentionTrace(traceFile);
        }
        catch (Exception ex)
        {
            _writeLine($"[{_getTheme().AccentColor}]Contention trace parse failed:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    public MemoryProfileResult BuildMemoryProfileResult(AllocationCallTreeResult callTree)
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

    public static void ApplyInputDefaults(
        string inputPath,
        ref bool runCpu,
        ref bool runMemory,
        ref bool runHeap,
        ref bool runException,
        ref bool runContention)
    {
        switch (GetNormalizedExtension(inputPath))
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

    private bool TryValidateTraceInput(string inputPath, string unsupportedMessage)
    {
        if (!TryEnsureInputExists(inputPath))
        {
            return false;
        }

        if (IsSupportedExtension(GetNormalizedExtension(inputPath), ".nettrace", ".etlx"))
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

    private static string GetNormalizedExtension(string inputPath)
    {
        return Path.GetExtension(inputPath).ToLowerInvariant();
    }

    private static bool IsSupportedExtension(string extension, params string[] allowedExtensions)
    {
        return allowedExtensions.Contains(extension, StringComparer.Ordinal);
    }
}
