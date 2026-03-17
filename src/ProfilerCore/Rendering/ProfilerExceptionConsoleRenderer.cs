using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerExceptionConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public ProfilerExceptionConsoleRenderer(Theme theme, ProfilerCallTreeRenderer callTreeRenderer)
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

        var selectedType = ProfileRenderFilters.SelectExceptionType(results.ExceptionTypes, request.ExceptionTypeFilter);
        var filteredExceptionTypes = ProfileRenderFilters.FilterExceptionTypes(results.ExceptionTypes, request.ExceptionTypeFilter);
        var hasTypeFilter = !string.IsNullOrWhiteSpace(request.ExceptionTypeFilter);
        ExceptionTypeDetails? selectedDetails = null;
        if (!string.IsNullOrWhiteSpace(selectedType) &&
            results.TypeDetails.TryGetValue(selectedType, out var details))
        {
            selectedDetails = details;
        }

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

        if (filteredExceptionTypes.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No exception events captured.[/]");
        }
        else
        {
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

        var summaryThrown = selectedDetails?.Thrown ?? results.TotalThrown;
        var summaryCaught = selectedDetails?.Caught ?? results.TotalCaught;
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

        if (summaryThrown > 0)
        {
            var resolvedRoot = ProfileRenderFilters.ResolveCallTreeRootFilter(request.CallTreeRoot);
            AnsiConsole.Write(_callTreeRenderer.BuildExceptionCallTree(new ProfilerExceptionCallTreeRequest(
                selectedDetails?.ThrowRoot ?? results.ThrowCallTreeRoot,
                summaryThrown,
                "Call Tree (Thrown Exceptions)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                ProfileRenderFilters.BuildTreeRootSelectionOptions(request, resolvedRoot))));
        }

        var catchSites = selectedDetails?.CatchSites ?? results.CatchSites;
        var catchRoot = selectedDetails?.CatchRoot ?? results.CatchCallTreeRoot;
        if (summaryCaught > 0 && catchRoot != null)
        {
            var filteredCatchSites = catchSites.Where(entry => ProfileRenderFilters.MatchesFunctionFilter(entry.Name, request.FunctionFilter));
            if (!request.IncludeRuntime)
            {
                filteredCatchSites = filteredCatchSites.Where(entry => !IsRuntimeNoise(entry.Name));
            }

            var catchList = filteredCatchSites.ToList();
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

            var resolvedRoot = ProfileRenderFilters.ResolveCallTreeRootFilter(request.CallTreeRoot);
            AnsiConsole.Write(_callTreeRenderer.BuildExceptionCallTree(new ProfilerExceptionCallTreeRequest(
                catchRoot,
                summaryCaught,
                "Call Tree (Catch Sites)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                ProfileRenderFilters.BuildTreeRootSelectionOptions(request, resolvedRoot))));
        }
    }
}
