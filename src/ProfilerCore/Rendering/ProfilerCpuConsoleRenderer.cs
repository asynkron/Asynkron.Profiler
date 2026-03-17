using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerCpuConsoleRenderer
{
    private const int AllocationTypeLimit = 3;
    private const int ExceptionTypeLimit = 3;

    private readonly Theme _theme;
    private readonly ProfilerConsoleTableWriter _tableWriter;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ProfilerCpuConsoleRenderer(
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
        if (results is null)
        {
            ProfilerRenderOutput.WriteNoResults(_theme);
            return;
        }

        ProfilerRenderOutput.WriteSection("CPU PROFILE", request);
        var resolvedRoot = ProfilerRenderFilters.NormalizeCallTreeRootFilter(request.CallTreeRoot);
        var allFunctions = results.AllFunctions;
        var totalTime = results.TotalTime;
        var timeUnitLabel = results.TimeUnitLabel;
        var countLabel = results.CountLabel;
        var countSuffix = results.CountSuffix;
        var allocationTypeLimit = results.CallTreeRoot.AllocationBytes > 0 ? AllocationTypeLimit : 0;
        var exceptionTypeLimit = results.CallTreeRoot.ExceptionCount > 0 ? ExceptionTypeLimit : 0;

        var filteredList = allFunctions
            .Where(entry => ProfilerRenderFilters.MatchesFunctionFilter(entry.Name, request.FunctionFilter))
            .Where(entry => ProfilerRenderFilters.IsVisibleAtRuntime(entry.Name, request.IncludeRuntime))
            .ToList();
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

        ProfilerRenderOutput.WriteFilteredOutRuntimeMessage(allFunctions.Count - filteredList.Count);
        ProfilerRenderOutput.WriteFunctionFilterMessage(request.FunctionFilter);

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

        RenderCpuCallTree(useSelfTime: false, allowTimeline: true);
        if (request.ShowSelfTimeTree)
        {
            RenderCpuCallTree(useSelfTime: true, allowTimeline: false);
        }

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
    }
}
