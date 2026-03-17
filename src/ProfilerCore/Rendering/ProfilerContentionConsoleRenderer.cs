using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerContentionConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ProfilerContentionConsoleRenderer(Theme theme, ProfilerCallTreeRenderer callTreeRenderer)
    {
        _theme = theme;
        _callTreeRenderer = callTreeRenderer;
    }

    public void Print(ContentionProfileResult? results, ProfileRenderRequest request)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        ConsoleThemeHelpers.PrintSection($"LOCK CONTENTION PROFILE: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }

        var filteredAll = results.TopFunctions.Where(entry => ProfileRenderRequestHelpers.MatchesFunctionFilter(entry.Name, request.FunctionFilter));
        if (!request.IncludeRuntime)
        {
            filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
        }

        var filteredList = filteredAll.ToList();
        var topTitle = request.IncludeRuntime && string.IsNullOrWhiteSpace(request.FunctionFilter)
            ? "Top Contended Functions (All)"
            : "Top Contended Functions (Filtered)";
        var rows = new List<IReadOnlyList<string>>();

        ConsoleThemeHelpers.PrintSection(topTitle);
        foreach (var entry in filteredList.Take(15))
        {
            var waitText = entry.TimeMs.ToString("F2", CultureInfo.InvariantCulture);
            var countText = entry.Calls.ToString("N0", CultureInfo.InvariantCulture);
            var funcText = FunctionDisplayFormatter.FormatFunctionCell(entry.Name, _theme.RuntimeTypeColor);
            rows.Add(new[]
            {
                $"[{_theme.CpuValueColor}]{waitText}[/]",
                $"[{_theme.CpuCountColor}]{countText}[/]",
                funcText
            });
        }

        ProfilerConsoleTableWriter.WriteTable(
            new[]
            {
                new TableColumnSpec("Wait (ms)", RightAligned: true),
                new TableColumnSpec("Count", RightAligned: true),
                new TableColumnSpec("Function")
            },
            rows);

        var filteredOut = results.TopFunctions.Count - filteredList.Count;
        if (filteredOut > 0)
        {
            var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
            AnsiConsole.MarkupLine($"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
        }

        if (!string.IsNullOrWhiteSpace(request.FunctionFilter))
        {
            AnsiConsole.MarkupLine($"[dim]Filter: {Markup.Escape(request.FunctionFilter)} (use --filter to change).[/]");
        }

        var summaryRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "[bold]Total Wait[/]",
                $"[{_theme.CpuValueColor}]{results.TotalWaitMs.ToString("F2", CultureInfo.InvariantCulture)} ms[/]"
            },
            new[]
            {
                "[bold]Total Contentions[/]",
                $"[{_theme.CpuCountColor}]{results.TotalCount.ToString("N0", CultureInfo.InvariantCulture)}[/]"
            }
        };
        ProfilerConsoleTableWriter.WriteSummaryTable(summaryRows);

        AnsiConsole.Write(_callTreeRenderer.BuildContentionCallTree(
            results,
            ProfileRenderRequestHelpers.ResolveCallTreeRootFilter(request.CallTreeRoot),
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent));
    }
}
