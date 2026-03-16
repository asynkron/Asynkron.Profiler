using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using static Asynkron.Profiler.CallTreeHelpers;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asynkron.Profiler;

public sealed partial class ProfilerConsoleRenderer
{
    private const int AllocationTypeLimit = 3;
    private const int ExceptionTypeLimit = 3;
    private static readonly TableColumnSpec[] SummaryColumns =
    {
        new(string.Empty),
        new(string.Empty)
    };

    private readonly Theme _theme;
    private readonly ProfileTreeRenderer _treeRenderer;
    private readonly Regex _jitNumberRegex = MyRegex();

    public ProfilerConsoleRenderer(Theme? theme = null)
    {
        _theme = theme ?? Theme.Current;
        _treeRenderer = new ProfileTreeRenderer(_theme);
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
            var funcName = FormatFunctionDisplayName(entry.Name);
            if (funcName.Length > 70)
            {
                funcName = funcName[..67] + "...";
            }

            var timeText = ProfileRenderFormatting.FormatCpuTime(entry.TimeMs, timeUnitLabel);
            var callsText = entry.Calls.ToString("N0", CultureInfo.InvariantCulture);
            rows.Add(
            [
                FormatRuntimeAwareFunctionText(funcName),
                $"[{_theme.CpuCountColor}]{callsText}[/]",
                $"[{_theme.CpuValueColor}]{timeText}[/]"
            ]);
        }

        var topTable = BuildTableWithRows(
            [
                new TableColumnSpec("Function"),
                new TableColumnSpec(countLabel, RightAligned: true),
                new TableColumnSpec(timeColumnLabel, RightAligned: true)
            ],
            rows);

        var allocationTable = memoryResults == null
            ? null
            : BuildAllocationTable(memoryResults.AllocationEntries, memoryResults.AllocationTotal);

        if (allocationTable != null)
        {
            var grid = new Grid();
            grid.AddColumn();
            grid.AddColumn();
            grid.AddRow(
                BuildTableBlock(topTable, topTitle, _theme.CpuCountColor),
                BuildTableBlock(allocationTable, "Allocation By Type (Sampled)", _theme.MemoryCountColor));
            AnsiConsole.Write(grid);
        }
        else
        {
            ConsoleThemeHelpers.PrintSection(topTitle, _theme.CpuCountColor);
            AnsiConsole.Write(topTable);
        }

        PrintFilterNotes(allFunctions.Count - filteredList.Count, request.FunctionFilter);

        var totalTimeText = ProfileRenderFormatting.FormatCpuTime(totalTime, timeUnitLabel);
        var hotCountText = allFunctions.Count.ToString(CultureInfo.InvariantCulture);
        var totalLabel = string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase)
            ? "Total Samples"
            : "Total Time";
        WriteSummaryTable(
        [
            [$"[bold]{totalLabel}[/]", $"[{_theme.CpuValueColor}]{totalTimeText} {timeUnitLabel}[/]"],
            ["[bold]Input Unit[/]", $"[{_theme.CpuValueColor}]{timeUnitLabel}[/]"],
            ["[bold]Hot Functions[/]", $"[{_theme.CpuCountColor}]{hotCountText}[/] functions profiled"]
        ]);

        void RenderCpuCallTree(bool useSelfTime, bool allowTimeline)
        {
            AnsiConsole.Write(_treeRenderer.BuildCpuCallTree(
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

    public void PrintMemoryResults(MemoryProfileResult? results, ProfileRenderRequest request)
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
                rows.Add([label, Markup.Escape(value)]);
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
            WriteTable(
            [
                new TableColumnSpec("Metric"),
                new TableColumnSpec("Value")
            ],
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
            PrintAllocationTable(results.AllocationEntries, results.AllocationTotal);
        }
        else if (!hasRows && !string.IsNullOrWhiteSpace(results.RawOutput))
        {
            AnsiConsole.WriteLine(results.RawOutput);
        }

        if (results.AllocationCallTree != null)
        {
            ConsoleThemeHelpers.PrintSection("Allocation Call Tree (Sampled)");
            AnsiConsole.Write(_treeRenderer.BuildAllocationCallTree(
                results.AllocationCallTree,
                request.CallTreeRoot,
                request.IncludeRuntime,
                request.CallTreeDepth,
                request.CallTreeWidth,
                request.CallTreeSiblingCutoffPercent));
        }
    }

    public void PrintExceptionResults(ExceptionProfileResult? results, ProfileRenderRequest request)
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
            AnsiConsole.MarkupLine(
                $"[{_theme.AccentColor}]No exception types matched '{Markup.Escape(request.ExceptionTypeFilter!)}'. Showing full results.[/]");
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
                rows.Add([$"[{_theme.CpuCountColor}]{countText}[/]", Markup.Escape(typeName)]);
            }

            WriteTable(
            [
                new TableColumnSpec("Count", RightAligned: true),
                new TableColumnSpec("Exception")
            ],
            rows);
        }

        var summaryThrown = selectedDetails?.Thrown ?? results.TotalThrown;
        var summaryCaught = selectedDetails?.Caught ?? results.TotalCaught;
        var summaryRows = new List<IReadOnlyList<string>>
        {
            new[] { "[bold]Thrown[/]", $"[{_theme.CpuValueColor}]{summaryThrown.ToString("N0", CultureInfo.InvariantCulture)}[/]" }
        };
        if (summaryCaught > 0)
        {
            summaryRows.Add(
            [
                "[bold]Caught[/]",
                $"[{_theme.CpuCountColor}]{summaryCaught.ToString("N0", CultureInfo.InvariantCulture)}[/]"
            ]);
        }

        WriteSummaryTable(summaryRows);

        if (summaryThrown > 0)
        {
            var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
            AnsiConsole.Write(_treeRenderer.BuildExceptionCallTree(
                selectedDetails?.ThrowRoot ?? results.ThrowCallTreeRoot,
                summaryThrown,
                "Call Tree (Thrown Exceptions)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                resolvedRoot,
                request.IncludeRuntime,
                request.CallTreeDepth,
                request.CallTreeWidth,
                request.CallTreeRootMode,
                request.CallTreeSiblingCutoffPercent));
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
                    var funcName = FormatFunctionDisplayName(entry.Name);
                    if (funcName.Length > 70)
                    {
                        funcName = funcName[..67] + "...";
                    }

                    var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
                    catchRows.Add([$"[{_theme.CpuCountColor}]{countText}[/]", FormatRuntimeAwareFunctionText(funcName)]);
                }

                WriteTable(
                [
                    new TableColumnSpec("Count", RightAligned: true),
                    new TableColumnSpec("Function")
                ],
                catchRows);
            }

            var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
            AnsiConsole.Write(_treeRenderer.BuildExceptionCallTree(
                catchRoot,
                summaryCaught,
                "Call Tree (Catch Sites)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                resolvedRoot,
                request.IncludeRuntime,
                request.CallTreeDepth,
                request.CallTreeWidth,
                request.CallTreeRootMode,
                request.CallTreeSiblingCutoffPercent));
        }
    }

    public void PrintContentionResults(ContentionProfileResult? results, ProfileRenderRequest request)
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
            var funcName = FormatFunctionDisplayName(entry.Name);
            if (funcName.Length > 70)
            {
                funcName = funcName[..67] + "...";
            }

            var waitText = entry.TimeMs.ToString("F2", CultureInfo.InvariantCulture);
            var countText = entry.Calls.ToString("N0", CultureInfo.InvariantCulture);
            rows.Add(
            [
                $"[{_theme.CpuValueColor}]{waitText}[/]",
                $"[{_theme.CpuCountColor}]{countText}[/]",
                FormatRuntimeAwareFunctionText(funcName)
            ]);
        }

        WriteTable(
        [
            new TableColumnSpec("Wait (ms)", RightAligned: true),
            new TableColumnSpec("Count", RightAligned: true),
            new TableColumnSpec("Function")
        ],
        rows);

        PrintFilterNotes(results.TopFunctions.Count - filteredList.Count, request.FunctionFilter);

        WriteSummaryTable(
        [
            ["[bold]Total Wait[/]", $"[{_theme.CpuValueColor}]{results.TotalWaitMs.ToString("F2", CultureInfo.InvariantCulture)} ms[/]"],
            ["[bold]Total Contentions[/]", $"[{_theme.CpuCountColor}]{results.TotalCount.ToString("N0", CultureInfo.InvariantCulture)}[/]"]
        ]);

        var resolvedRoot = ResolveCallTreeRootFilter(request.CallTreeRoot);
        AnsiConsole.Write(_treeRenderer.BuildContentionCallTree(
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
                var typeName = entry.Type.Length > 60 ? entry.Type[..57] + "..." : entry.Type;
                rows.Add(
                [
                    entry.Size.ToString("N0", CultureInfo.InvariantCulture),
                    entry.Count.ToString("N0", CultureInfo.InvariantCulture),
                    Markup.Escape(typeName)
                ]);
            }

            WriteTable(
            [
                new TableColumnSpec("Size (bytes)", RightAligned: true),
                new TableColumnSpec("Count", RightAligned: true),
                new TableColumnSpec("Type")
            ],
            rows);
        }
        else if (!string.IsNullOrWhiteSpace(results.RawOutput))
        {
            AnsiConsole.WriteLine(results.RawOutput);
        }
    }

    private string FormatRuntimeAwareFunctionText(string funcName)
    {
        var funcText = Markup.Escape(funcName);
        if (IsUnmanagedFrame(funcName))
        {
            funcText = $"[{_theme.RuntimeTypeColor}]{funcText}[/]";
        }

        return funcText;
    }

    private static void PrintFilterNotes(int filteredOut, string? functionFilter)
    {
        if (filteredOut > 0)
        {
            var filteredOutText = filteredOut.ToString("N0", CultureInfo.InvariantCulture);
            AnsiConsole.MarkupLine(
                $"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
        }

        if (!string.IsNullOrWhiteSpace(functionFilter))
        {
            AnsiConsole.MarkupLine(
                $"[dim]Filter: {Markup.Escape(functionFilter)} (use --filter to change).[/]");
        }
    }

    private static Table BuildTable(
        IReadOnlyList<TableColumnSpec> columns,
        string? title = null,
        bool hideHeaders = false,
        string? headerColor = null)
    {
        var table = new Table
        {
            Expand = false,
            ShowHeaders = !hideHeaders,
            ShowRowSeparators = false,
            Title = title != null ? new TableTitle(title) : null
        };
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Grey));

        foreach (var column in columns)
        {
            var columnHeader = column.Header;
            if (!string.IsNullOrWhiteSpace(headerColor))
            {
                columnHeader = $"[{headerColor}]{Markup.Escape(columnHeader)}[/]";
            }

            var tableColumn = new TableColumn(columnHeader);
            if (column.RightAligned)
            {
                tableColumn.RightAligned();
            }

            table.AddColumn(tableColumn);
        }

        return table;
    }

    private static Table BuildTableWithRows(
        IReadOnlyList<TableColumnSpec> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        string? title = null,
        bool hideHeaders = false,
        string? headerColor = null)
    {
        var table = BuildTable(columns, title, hideHeaders, headerColor);
        foreach (var row in rows)
        {
            table.AddRow(row.ToArray());
        }

        return table;
    }

    private static void WriteTable(
        IReadOnlyList<TableColumnSpec> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        string? title = null,
        bool hideHeaders = false,
        string? headerColor = null)
    {
        AnsiConsole.Write(BuildTableWithRows(columns, rows, title, hideHeaders, headerColor));
    }

    private static void WriteSummaryTable(IEnumerable<IReadOnlyList<string>> rows)
    {
        ConsoleThemeHelpers.PrintSection("Summary");
        WriteTable(SummaryColumns, rows, hideHeaders: true);
    }

    private static Rows BuildTableBlock(Table table, string title, string color)
    {
        return new Rows(new Markup($"[{color}]{Markup.Escape(title)}[/]"), table);
    }

    private static string? ResolveCallTreeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }

    private Table? BuildAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        long totalCount = 0;
        var rows = new List<IReadOnlyList<string>>();

        foreach (var entry in entries)
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Type);
            if (typeName.Length > 80)
            {
                typeName = typeName[..77] + "...";
            }

            var totalText = entry.Total ?? string.Empty;
            var paddedTotalText = totalText.Length == 0 ? totalText : " " + totalText;
            totalCount += entry.Count;

            rows.Add(
            [
                $"[{_theme.TextColor}]{Markup.Escape(typeName)}[/]",
                $"[{_theme.MemoryCountColor}]{Markup.Escape(entry.Count.ToString("N0", CultureInfo.InvariantCulture))}[/]",
                $"[{_theme.MemoryValueColor}]{Markup.Escape(paddedTotalText)}[/]"
            ]);
        }

        if (!string.IsNullOrWhiteSpace(allocationTotal))
        {
            rows.Add(
            [
                $"[bold {_theme.TextColor}]TOTAL (shown)[/]",
                $"[bold {_theme.MemoryCountColor}]{Markup.Escape(totalCount.ToString("N0", CultureInfo.InvariantCulture))}[/]",
                $"[bold {_theme.MemoryValueColor}]{Markup.Escape(" " + allocationTotal)}[/]"
            ]);
        }

        return BuildTableWithRows(
        [
            new TableColumnSpec("Type"),
            new TableColumnSpec("Count", RightAligned: true),
            new TableColumnSpec(" Total", RightAligned: true)
        ],
        rows);
    }

    private void PrintAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
    {
        var table = BuildAllocationTable(entries, allocationTotal);
        if (table != null)
        {
            AnsiConsole.Write(table);
        }
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

    public string HighlightJitNumbers(string text)
    {
        return _jitNumberRegex.Replace(text, match => ProfileRenderFormatting.WrapMarkup(match.Value, _theme.AccentColor));
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}

public sealed record TableColumnSpec(string Header, bool RightAligned = false);
