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
    private readonly ProfilerCallTreeFormatter _callTreeFormatter;

    public ProfilerConsoleRenderer(Theme? theme = null)
    {
        _theme = theme ?? Theme.Current;
        _tableWriter = new ProfilerConsoleTableWriter(_theme);
        _callTreeFormatter = new ProfilerCallTreeFormatter(_theme);
        _callTreeRenderer = new ProfilerCallTreeRenderer(_theme, _callTreeFormatter);
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

        var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
        var allFunctions = results.AllFunctions;
        var totalTime = results.TotalTime;
        var timeUnitLabel = results.TimeUnitLabel;
        var countLabel = results.CountLabel;
        var countSuffix = results.CountSuffix;
        var allocationTypeLimit = results.CallTreeRoot.AllocationBytes > 0 ? AllocationTypeLimit : 0;
        var exceptionTypeLimit = results.CallTreeRoot.ExceptionCount > 0 ? ExceptionTypeLimit : 0;

        var filteredAll = allFunctions.Where(entry => MatchesFunctionFilter(entry.Name, request.FunctionFilter));
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

        var selectedType = SelectExceptionType(results.ExceptionTypes, request.ExceptionTypeFilter);
        var filteredExceptionTypes = FilterExceptionTypes(results.ExceptionTypes, request.ExceptionTypeFilter);
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
            var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
            AnsiConsole.Write(_callTreeRenderer.BuildExceptionCallTree(new ProfilerExceptionCallTreeRequest(
                selectedDetails?.ThrowRoot ?? results.ThrowCallTreeRoot,
                summaryThrown,
                "Call Tree (Thrown Exceptions)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                BuildTreeRootSelectionOptions(request, resolvedRoot))));
        }

        var catchSites = selectedDetails?.CatchSites ?? results.CatchSites;
        var catchRoot = selectedDetails?.CatchRoot ?? results.CatchCallTreeRoot;
        if (summaryCaught > 0 && catchRoot != null)
        {
            var filteredCatchSites = catchSites.Where(entry => MatchesFunctionFilter(entry.Name, request.FunctionFilter));
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

            var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
            AnsiConsole.Write(_callTreeRenderer.BuildExceptionCallTree(new ProfilerExceptionCallTreeRequest(
                catchRoot,
                summaryCaught,
                "Call Tree (Catch Sites)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                BuildTreeRootSelectionOptions(request, resolvedRoot))));
        }
    }

    public void PrintContentionResults(
        ContentionProfileResult? results,
        ProfileRenderRequest request)
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

        var filteredAll = results.TopFunctions.Where(entry => MatchesFunctionFilter(entry.Name, request.FunctionFilter));
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

        var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
        AnsiConsole.Write(_callTreeRenderer.BuildContentionCallTree(
            results,
            resolvedRoot,
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent));
    }

    public void PrintHeapResults(HeapProfileResult? results, ProfileRenderRequest request)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        ConsoleThemeHelpers.PrintSection($"HEAP SNAPSHOT: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }

        if (results.Types.Count > 0)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var entry in results.Types.Take(40))
            {
                var sizeText = entry.Size.ToString("N0", CultureInfo.InvariantCulture);
                var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
                var typeName = entry.Type.Length > 60 ? entry.Type[..57] + "..." : entry.Type;
                rows.Add(new[]
                {
                    sizeText,
                    countText,
                    Markup.Escape(typeName)
                });
            }

            ProfilerConsoleTableWriter.WriteTable(
                new[]
                {
                    new TableColumnSpec("Size (bytes)", RightAligned: true),
                    new TableColumnSpec("Count", RightAligned: true),
                    new TableColumnSpec("Type")
                },
                rows);
        }
        else if (!string.IsNullOrWhiteSpace(results.RawOutput))
        {
            AnsiConsole.WriteLine(results.RawOutput);
        }
    }

    private static string? ResolveCallTreeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }

    private static ProfilerTreeRootSelectionOptions BuildTreeRootSelectionOptions(
        ProfileRenderRequest request,
        string? rootFilter)
    {
        return new ProfilerTreeRootSelectionOptions(
            rootFilter,
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent);
    }

    private static bool MatchesFunctionFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               FormatFunctionDisplayName(name).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ExceptionTypeSample> FilterExceptionTypes(
        IReadOnlyList<ExceptionTypeSample> types,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return types;
        }

        return types
            .Where(entry => entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            NameFormatter.FormatTypeDisplayName(entry.Type)
                                .Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? SelectExceptionType(IReadOnlyList<ExceptionTypeSample> types, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        foreach (var entry in types)
        {
            if (entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                NameFormatter.FormatTypeDisplayName(entry.Type)
                    .Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Type;
            }
        }

        return null;
    }

    public string HighlightJitNumbers(string text) => _callTreeRenderer.HighlightJitNumbers(text);
}

public sealed record TableColumnSpec(string Header, bool RightAligned = false);
