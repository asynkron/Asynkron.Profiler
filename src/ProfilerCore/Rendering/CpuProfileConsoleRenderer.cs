using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class CpuProfileConsoleRenderer
{
    private const int AllocationTypeLimit = 3;
    private const int ExceptionTypeLimit = 3;

    private readonly Theme _theme;
    private readonly ProfilerConsoleTableWriter _tableWriter;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public CpuProfileConsoleRenderer(
        Theme theme,
        ProfilerConsoleTableWriter tableWriter,
        ProfilerCallTreeRenderer callTreeRenderer)
    {
        _theme = theme;
        _tableWriter = tableWriter;
        _callTreeRenderer = callTreeRenderer;
    }

    public void Print(
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

        var allFunctions = results.AllFunctions;
        var timeUnitLabel = results.TimeUnitLabel;
        var countLabel = results.CountLabel;
        var countSuffix = results.CountSuffix;
        var allocationTypeLimit = results.CallTreeRoot.AllocationBytes > 0 ? AllocationTypeLimit : 0;
        var exceptionTypeLimit = results.CallTreeRoot.ExceptionCount > 0 ? ExceptionTypeLimit : 0;
        var filteredList = FilterFunctions(allFunctions, request).ToList();
        var topTitle = request.IncludeRuntime && string.IsNullOrWhiteSpace(request.FunctionFilter)
            ? "Top Functions (All)"
            : "Top Functions (Filtered)";
        var timeColumnLabel = string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase)
            ? "Samples"
            : $"Time ({timeUnitLabel})";

        RenderTopFunctions(filteredList, countLabel, timeUnitLabel, timeColumnLabel, topTitle, memoryResults);
        WriteFilterSummary(allFunctions.Count, filteredList.Count, request.FunctionFilter);
        RenderSummary(results.TotalTime, allFunctions.Count, timeUnitLabel);
        RenderCallTree(results, request, countSuffix, timeUnitLabel, allocationTypeLimit, exceptionTypeLimit);
    }

    private IEnumerable<FunctionSample> FilterFunctions(
        IReadOnlyList<FunctionSample> allFunctions,
        ProfileRenderRequest request)
    {
        var filteredAll = allFunctions.Where(entry => ProfilerRenderFilters.MatchesFunctionFilter(entry.Name, request.FunctionFilter));
        if (!request.IncludeRuntime)
        {
            filteredAll = filteredAll.Where(entry => !CallTreeHelpers.IsRuntimeNoise(entry.Name));
        }

        return filteredAll;
    }

    private void RenderTopFunctions(
        IReadOnlyList<FunctionSample> entries,
        string countLabel,
        string timeUnitLabel,
        string timeColumnLabel,
        string topTitle,
        MemoryProfileResult? memoryResults)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var entry in entries.Take(15))
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

        if (allocationTable == null)
        {
            ConsoleThemeHelpers.PrintSection(topTitle, _theme.CpuCountColor);
            AnsiConsole.Write(topTable);
            return;
        }

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(
            ProfilerConsoleTableWriter.BuildTableBlock(topTable, topTitle, _theme.CpuCountColor),
            ProfilerConsoleTableWriter.BuildTableBlock(allocationTable, "Allocation By Type (Sampled)", _theme.MemoryCountColor));
        AnsiConsole.Write(grid);
    }

    private void WriteFilterSummary(int totalFunctions, int filteredCount, string? functionFilter)
    {
        var filteredOut = totalFunctions - filteredCount;
        if (filteredOut > 0)
        {
            var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
            AnsiConsole.MarkupLine($"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
        }

        if (!string.IsNullOrWhiteSpace(functionFilter))
        {
            AnsiConsole.MarkupLine($"[dim]Filter: {Markup.Escape(functionFilter)} (use --filter to change).[/]");
        }
    }

    private void RenderSummary(double totalTime, int hotFunctionCount, string timeUnitLabel)
    {
        var totalTimeText = ProfilerCallTreeFormatter.FormatCpuTime(totalTime, timeUnitLabel);
        var hotCountText = hotFunctionCount.ToString(CultureInfo.InvariantCulture);
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
    }

    private void RenderCallTree(
        CpuProfileResult results,
        ProfileRenderRequest request,
        string countSuffix,
        string timeUnitLabel,
        int allocationTypeLimit,
        int exceptionTypeLimit)
    {
        var rootFilter = ProfilerTreeRootSelectionFactory.NormalizeRootFilter(request.CallTreeRoot);

        WriteCpuCallTree(
            results,
            request,
            countSuffix,
            timeUnitLabel,
            allocationTypeLimit,
            exceptionTypeLimit,
            rootFilter,
            useSelfTime: false,
            showTimeline: request.ShowTimeline);

        if (!request.ShowSelfTimeTree)
        {
            return;
        }

        WriteCpuCallTree(
            results,
            request,
            countSuffix,
            timeUnitLabel,
            allocationTypeLimit,
            exceptionTypeLimit,
            rootFilter,
            useSelfTime: true,
            showTimeline: false);
    }

    private void WriteCpuCallTree(
        CpuProfileResult results,
        ProfileRenderRequest request,
        string countSuffix,
        string timeUnitLabel,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        string? rootFilter,
        bool useSelfTime,
        bool showTimeline)
    {
        AnsiConsole.Write(_callTreeRenderer.BuildCpuCallTree(
            results,
            useSelfTime,
            rootFilter,
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
            showTimeline,
            request.TimelineWidth));
    }
}
