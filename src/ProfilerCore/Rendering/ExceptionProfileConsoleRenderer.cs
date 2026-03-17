using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ExceptionProfileConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ExceptionProfileConsoleRenderer(Theme theme, ProfilerCallTreeRenderer callTreeRenderer)
    {
        _theme = theme;
        _callTreeRenderer = callTreeRenderer;
    }

    public void Print(ExceptionProfileResult? results, ProfileRenderRequest request)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        ConsoleThemeHelpers.PrintSection($"EXCEPTION PROFILE: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }

        var selectedType = ProfilerRenderFilters.SelectExceptionType(results.ExceptionTypes, request.ExceptionTypeFilter);
        var filteredExceptionTypes = ProfilerRenderFilters.FilterExceptionTypes(results.ExceptionTypes, request.ExceptionTypeFilter);
        var hasTypeFilter = !string.IsNullOrWhiteSpace(request.ExceptionTypeFilter);
        var selectedDetails = ResolveSelectedDetails(results, selectedType);

        if (hasTypeFilter && filteredExceptionTypes.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No exception types matched '{Markup.Escape(request.ExceptionTypeFilter!)}'. Showing full results.[/]");
            filteredExceptionTypes = results.ExceptionTypes;
            selectedType = null;
            selectedDetails = null;
        }
        else if (hasTypeFilter && !string.IsNullOrWhiteSpace(selectedType))
        {
            AnsiConsole.MarkupLine($"[dim]Exception type filter: {Markup.Escape(selectedType)}[/]");
        }

        RenderTopExceptions(filteredExceptionTypes);

        var summaryThrown = selectedDetails?.Thrown ?? results.TotalThrown;
        var summaryCaught = selectedDetails?.Caught ?? results.TotalCaught;
        RenderSummary(summaryThrown, summaryCaught);
        RenderThrownCallTree(results, request, selectedType, selectedDetails, summaryThrown);
        RenderCatchFlow(results, request, selectedType, selectedDetails, summaryCaught);
    }

    private static ExceptionTypeDetails? ResolveSelectedDetails(ExceptionProfileResult results, string? selectedType)
    {
        if (string.IsNullOrWhiteSpace(selectedType))
        {
            return null;
        }

        return results.TypeDetails.TryGetValue(selectedType, out var details)
            ? details
            : null;
    }

    private void RenderTopExceptions(IReadOnlyList<ExceptionTypeSample> filteredExceptionTypes)
    {
        if (filteredExceptionTypes.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No exception events captured.[/]");
            return;
        }

        ConsoleThemeHelpers.PrintSection("Top Exceptions (Thrown)");
        var rows = new List<IReadOnlyList<string>>();

        foreach (var entry in filteredExceptionTypes.Take(15))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Type);
            if (typeName.Length > 70)
            {
                typeName = typeName[..67] + "...";
            }

            var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
            rows.Add(new[]
            {
                $"[{_theme.CpuCountColor}]{countText}[/]",
                Markup.Escape(typeName)
            });
        }

        ProfilerConsoleTableWriter.WriteTable(
            new[]
            {
                new TableColumnSpec("Count", RightAligned: true),
                new TableColumnSpec("Exception")
            },
            rows);
    }

    private void RenderSummary(long summaryThrown, long summaryCaught)
    {
        var thrownText = summaryThrown.ToString("N0", CultureInfo.InvariantCulture);
        var summaryRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "[bold]Thrown[/]",
                $"[{_theme.CpuValueColor}]{thrownText}[/]"
            }
        };

        if (summaryCaught > 0)
        {
            var caughtText = summaryCaught.ToString("N0", CultureInfo.InvariantCulture);
            summaryRows.Add(new[]
            {
                "[bold]Caught[/]",
                $"[{_theme.CpuCountColor}]{caughtText}[/]"
            });
        }

        ProfilerConsoleTableWriter.WriteSummaryTable(summaryRows);
    }

    private void RenderThrownCallTree(
        ExceptionProfileResult results,
        ProfileRenderRequest request,
        string? selectedType,
        ExceptionTypeDetails? selectedDetails,
        long summaryThrown)
    {
        if (summaryThrown <= 0)
        {
            return;
        }

        AnsiConsole.Write(_callTreeRenderer.BuildExceptionCallTree(new ProfilerExceptionCallTreeRequest(
            selectedDetails?.ThrowRoot ?? results.ThrowCallTreeRoot,
            summaryThrown,
            "Call Tree (Thrown Exceptions)",
            selectedType == null ? null : NameFormatter.FormatTypeDisplayName(selectedType),
            ProfilerTreeRootSelectionFactory.Build(request))));
    }

    private void RenderCatchFlow(
        ExceptionProfileResult results,
        ProfileRenderRequest request,
        string? selectedType,
        ExceptionTypeDetails? selectedDetails,
        long summaryCaught)
    {
        var catchSites = selectedDetails?.CatchSites ?? results.CatchSites;
        var catchRoot = selectedDetails?.CatchRoot ?? results.CatchCallTreeRoot;
        if (summaryCaught <= 0 || catchRoot == null)
        {
            return;
        }

        var catchList = catchSites
            .Where(entry => ProfilerRenderFilters.MatchesFunctionFilter(entry.Name, request.FunctionFilter))
            .Where(entry => request.IncludeRuntime || !CallTreeHelpers.IsRuntimeNoise(entry.Name))
            .ToList();

        if (catchList.Count > 0)
        {
            ConsoleThemeHelpers.PrintSection("Top Catch Sites");
            var catchRows = new List<IReadOnlyList<string>>();

            foreach (var entry in catchList.Take(15))
            {
                var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
                var funcText = FunctionDisplayFormatter.FormatFunctionCell(entry.Name, _theme.RuntimeTypeColor);
                catchRows.Add(new[]
                {
                    $"[{_theme.CpuCountColor}]{countText}[/]",
                    funcText
                });
            }

            ProfilerConsoleTableWriter.WriteTable(
                new[]
                {
                    new TableColumnSpec("Count", RightAligned: true),
                    new TableColumnSpec("Function")
                },
                catchRows);
        }

        AnsiConsole.Write(_callTreeRenderer.BuildExceptionCallTree(new ProfilerExceptionCallTreeRequest(
            catchRoot,
            summaryCaught,
            "Call Tree (Catch Sites)",
            selectedType == null ? null : NameFormatter.FormatTypeDisplayName(selectedType),
            ProfilerTreeRootSelectionFactory.Build(request))));
    }
}
