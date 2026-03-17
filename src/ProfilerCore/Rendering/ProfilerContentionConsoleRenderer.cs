using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerContentionConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ProfilerContentionConsoleRenderer(
        Theme theme,
        ProfilerCallTreeRenderer callTreeRenderer)
    {
        _theme = theme;
        _callTreeRenderer = callTreeRenderer;
    }

    public void Print(ContentionProfileResult? results, ProfileRenderRequest request)
    {
        if (results is null)
        {
            ProfilerRenderOutput.WriteNoResults(_theme);
            return;
        }

        ProfilerRenderOutput.WriteSection("LOCK CONTENTION PROFILE", request);
        var filteredList = results.TopFunctions
            .Where(entry => ProfilerRenderFilters.MatchesFunctionFilter(entry.Name, request.FunctionFilter))
            .Where(entry => ProfilerRenderFilters.IsVisibleAtRuntime(entry.Name, request.IncludeRuntime))
            .ToList();
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

        ProfilerRenderOutput.WriteFilteredOutRuntimeMessage(results.TopFunctions.Count - filteredList.Count);
        ProfilerRenderOutput.WriteFunctionFilterMessage(request.FunctionFilter);

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

        var resolvedRoot = ProfilerRenderFilters.NormalizeCallTreeRootFilter(request.CallTreeRoot);
        AnsiConsole.Write(_callTreeRenderer.BuildContentionCallTree(
            results,
            resolvedRoot,
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent));
    }
}
