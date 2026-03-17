using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

public sealed class ProfilerConsoleRenderer
{
    private const int AllocationTypeLimit = 3;
    private const int ExceptionTypeLimit = 3;

    private readonly Theme _theme;
    private readonly ProfilerConsoleTableWriter _tableWriter;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;
    private readonly ProfilerExceptionConsoleRenderer _exceptionRenderer;
    private readonly ProfilerContentionConsoleRenderer _contentionRenderer;
    private readonly ProfilerHeapConsoleRenderer _heapRenderer;

    public ProfilerConsoleRenderer(Theme? theme = null)
    {
        _theme = theme ?? Theme.Current;
        _tableWriter = new ProfilerConsoleTableWriter(_theme);
        var callTreeFormatter = new ProfilerCallTreeFormatter(_theme);
        _callTreeRenderer = new ProfilerCallTreeRenderer(_theme, callTreeFormatter);
        _exceptionRenderer = new ProfilerExceptionConsoleRenderer(_theme, _callTreeRenderer);
        _contentionRenderer = new ProfilerContentionConsoleRenderer(_theme, _callTreeRenderer);
        _heapRenderer = new ProfilerHeapConsoleRenderer();
    }

    public Theme Theme => _theme;

    public void PrintCpuResults(
        CpuProfileResult? results,
        ProfileRenderRequest request,
        MemoryProfileResult? memoryResults = null)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        ConsoleThemeHelpers.PrintSection($"CPU PROFILE: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }

        var resolvedRoot = ProfileRenderRequestHelpers.ResolveCallTreeRootFilter(request.CallTreeRoot);
        var allFunctions = results.AllFunctions;
        var totalTime = results.TotalTime;
        var timeUnitLabel = results.TimeUnitLabel;
        var countLabel = results.CountLabel;
        var countSuffix = results.CountSuffix;
        var allocationTypeLimit = results.CallTreeRoot.AllocationBytes > 0 ? AllocationTypeLimit : 0;
        var exceptionTypeLimit = results.CallTreeRoot.ExceptionCount > 0 ? ExceptionTypeLimit : 0;

        var filteredAll = allFunctions.Where(entry => ProfileRenderRequestHelpers.MatchesFunctionFilter(entry.Name, request.FunctionFilter));
        if (!request.IncludeRuntime)
        {
            filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
        }

        var filteredList = filteredAll.ToList();
        var topTitle = request.IncludeRuntime && string.IsNullOrWhiteSpace(request.FunctionFilter)
            ? "Top Functions (All)"
            : "Top Functions (Filtered)";
        var timeColumnLabel = string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase)
            ? "Samples"
            : $"Time ({timeUnitLabel})";
        var rows = new List<IReadOnlyList<string>>();

        foreach (var entry in filteredList.Take(15))
        {
            var timeMsText = ProfilerCallTreeFormatter.FormatCpuTime(entry.TimeMs, timeUnitLabel);
            var callsText = entry.Calls.ToString("N0", CultureInfo.InvariantCulture);
            var funcText = FunctionDisplayFormatter.FormatFunctionCell(entry.Name, _theme.RuntimeTypeColor);

            rows.Add(new[]
            {
                funcText,
                $"[{_theme.CpuCountColor}]{callsText}[/]",
                $"[{_theme.CpuValueColor}]{timeMsText}[/]"
            });
        }

        var topTable = ProfilerConsoleTableWriter.BuildTableWithRows(
            new[]
            {
                new TableColumnSpec("Function"),
                new TableColumnSpec(countLabel, RightAligned: true),
                new TableColumnSpec(timeColumnLabel, RightAligned: true)
            },
            rows);

        var allocationTable = memoryResults == null
            ? null
            : _tableWriter.BuildAllocationTable(memoryResults.AllocationEntries, memoryResults.AllocationTotal);

        if (allocationTable != null)
        {
            var grid = new Grid();
            grid.AddColumn();
            grid.AddColumn();
            grid.AddRow(
                ProfilerConsoleTableWriter.BuildTableBlock(topTable, topTitle, _theme.CpuCountColor),
                ProfilerConsoleTableWriter.BuildTableBlock(allocationTable, "Allocation By Type (Sampled)", _theme.MemoryCountColor));
            AnsiConsole.Write(grid);
        }
        else
        {
            ConsoleThemeHelpers.PrintSection(topTitle, _theme.CpuCountColor);
            AnsiConsole.Write(topTable);
        }

        var filteredOut = allFunctions.Count - filteredList.Count;
        if (filteredOut > 0)
        {
            var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
            AnsiConsole.MarkupLine($"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
        }

        if (!string.IsNullOrWhiteSpace(request.FunctionFilter))
        {
            AnsiConsole.MarkupLine($"[dim]Filter: {Markup.Escape(request.FunctionFilter)} (use --filter to change).[/]");
        }

        var totalTimeText = ProfilerCallTreeFormatter.FormatCpuTime(totalTime, timeUnitLabel);
        var hotCountText = allFunctions.Count.ToString(CultureInfo.InvariantCulture);
        var totalLabel = string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase)
            ? "Total Samples"
            : "Total Time";
        var summaryRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                $"[bold]{totalLabel}[/]",
                $"[{_theme.CpuValueColor}]{totalTimeText} {timeUnitLabel}[/]"
            },
            new[]
            {
                "[bold]Input Unit[/]",
                $"[{_theme.CpuValueColor}]{timeUnitLabel}[/]"
            },
            new[]
            {
                "[bold]Hot Functions[/]",
                $"[{_theme.CpuCountColor}]{hotCountText}[/] functions profiled"
            }
        };
        ProfilerConsoleTableWriter.WriteSummaryTable(summaryRows);

        void RenderCpuCallTree(bool useSelfTime, bool allowTimeline)
        {
            AnsiConsole.Write(_callTreeRenderer.BuildCpuCallTree(
                results,
                useSelfTime,
                resolvedRoot,
                request.IncludeRuntime,
                request.CallTreeDepth,
                request.CallTreeWidth,
                request.CallTreeRootMode,
                request.CallTreeSiblingCutoffPercent,
                timeUnitLabel,
                countSuffix,
                allocationTypeLimit,
                exceptionTypeLimit,
                request.HotThreshold,
                showTimeline: allowTimeline && request.ShowTimeline,
                timelineWidth: request.TimelineWidth));
        }

        RenderCpuCallTree(useSelfTime: false, allowTimeline: true);
        if (request.ShowSelfTimeTree)
        {
            RenderCpuCallTree(useSelfTime: true, allowTimeline: false);
        }
    }

    public void PrintMemoryResults(
        MemoryProfileResult? results,
        ProfileRenderRequest request)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        ConsoleThemeHelpers.PrintSection($"MEMORY PROFILE: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }

        var rows = new List<IReadOnlyList<string>>();

        void AddRow(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new[] { label, Markup.Escape(value) });
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

        var hasRows = rows.Count > 0;
        if (hasRows)
        {
            ProfilerConsoleTableWriter.WriteTable(
                new[]
                {
                    new TableColumnSpec("Metric"),
                    new TableColumnSpec("Value")
                },
                rows);
        }

        if (!string.IsNullOrWhiteSpace(results.AllocationByTypeRaw))
        {
            ConsoleThemeHelpers.PrintSection("Allocation By Type (Sampled)", _theme.MemoryCountColor);
            AnsiConsole.WriteLine(results.AllocationByTypeRaw);
        }
        else if (results.AllocationEntries.Count > 0)
        {
            ConsoleThemeHelpers.PrintSection("Allocation By Type (Sampled)", _theme.MemoryCountColor);
            _tableWriter.PrintAllocationTable(results.AllocationEntries, results.AllocationTotal);
        }
        else if (!hasRows && !string.IsNullOrWhiteSpace(results.RawOutput))
        {
            AnsiConsole.WriteLine(results.RawOutput);
        }

        if (results.AllocationCallTree != null)
        {
            ConsoleThemeHelpers.PrintSection("Allocation Call Tree (Sampled)");
            _callTreeRenderer.PrintAllocationCallTree(new ProfilerAllocationCallTreeRequest(
                results.AllocationCallTree,
                request.CallTreeRoot,
                request.IncludeRuntime,
                CallTreeTraversalSettings.Create(
                    request.CallTreeDepth,
                    request.CallTreeWidth,
                    request.CallTreeSiblingCutoffPercent)));
        }
    }

    public void PrintExceptionResults(
        ExceptionProfileResult? results,
        ProfileRenderRequest request)
    {
        _exceptionRenderer.Print(results, request);
    }

    public void PrintContentionResults(
        ContentionProfileResult? results,
        ProfileRenderRequest request)
    {
        _contentionRenderer.Print(results, request);
    }

    public void PrintHeapResults(HeapProfileResult? results, ProfileRenderRequest request)
    {
        _heapRenderer.Print(_theme, results, request);
    }

    public string HighlightJitNumbers(string text) => _callTreeRenderer.HighlightJitNumbers(text);
}
