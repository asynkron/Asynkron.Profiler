using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ContentionProfileConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ContentionProfileConsoleRenderer(Theme theme, ProfilerCallTreeRenderer callTreeRenderer)
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

        var filteredList = results.TopFunctions
            .Where(entry => ProfilerRenderFilters.MatchesFunctionFilter(entry.Name, request.FunctionFilter))
            .Where(entry => request.IncludeRuntime || !CallTreeHelpers.IsRuntimeNoise(entry.Name))
            .ToList();
        var topTitle = request.IncludeRuntime && string.IsNullOrWhiteSpace(request.FunctionFilter)
            ? "Top Contended Functions (All)"
            : "Top Contended Functions (Filtered)";

        ConsoleThemeHelpers.PrintSection(topTitle);
        RenderTopFunctions(filteredList);
        WriteFilterSummary(results.TopFunctions.Count, filteredList.Count, request.FunctionFilter);
        RenderSummary(results);
        AnsiConsole.Write(_callTreeRenderer.BuildContentionCallTree(
            results,
            ProfilerTreeRootSelectionFactory.NormalizeRootFilter(request.CallTreeRoot),
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent));
    }

    private void RenderTopFunctions(IReadOnlyList<FunctionSample> filteredList)
    {
        var rows = new List<IReadOnlyList<string>>();
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
    }

    private static void WriteFilterSummary(int totalFunctions, int filteredCount, string? functionFilter)
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

    private void RenderSummary(ContentionProfileResult results)
    {
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
    }
}
