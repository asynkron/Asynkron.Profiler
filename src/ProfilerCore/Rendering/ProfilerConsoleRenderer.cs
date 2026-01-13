using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asynkron.Profiler;

public sealed class ProfilerConsoleRenderer
{
    private const int AllocationTypeLimit = 3;
    private const int ExceptionTypeLimit = 3;
    private const double HotnessFireThreshold = 0.4d;
    private const double HotnessColorFloor = 0.001d;
    private const double HotnessColorMid = 0.0025d;
    private const double HotnessColorMax = 0.4d;
    private const string HotspotMarker = "\U0001F525";

    private readonly Theme _theme;
    private Style _treeGuideStyle;
    private readonly Regex _jitNumberRegex = new(
        @"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])",
        RegexOptions.Compiled);

    public ProfilerConsoleRenderer(Theme? theme = null)
    {
        _theme = theme ?? Theme.Current;
        _treeGuideStyle = new Style(ParseColor(_theme.TreeGuideColor));
    }

    public Theme Theme => _theme;

    public void PrintCpuResults(
        CpuProfileResult? results,
        string profileName,
        string? description,
        string? rootFilter,
        string? functionFilter,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        string? callTreeRootMode,
        bool showSelfTimeTree,
        int callTreeSiblingCutoffPercent,
        double hotThreshold,
        bool showTimeline = false,
        int timelineWidth = 40,
        MemoryProfileResult? memoryResults = null)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        PrintSection($"CPU PROFILE: {profileName}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            AnsiConsole.MarkupLine($"[dim]{description}[/]");
        }

        var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
        var allFunctions = results.AllFunctions;
        var totalTime = results.TotalTime;
        var timeUnitLabel = results.TimeUnitLabel;
        var countLabel = results.CountLabel;
        var countSuffix = results.CountSuffix;
        var allocationTypeLimit = results.CallTreeRoot.AllocationBytes > 0 ? AllocationTypeLimit : 0;
        var exceptionTypeLimit = results.CallTreeRoot.ExceptionCount > 0 ? ExceptionTypeLimit : 0;

        var filteredAll = allFunctions.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
        if (!includeRuntime)
        {
            filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
        }
        var filteredList = filteredAll.ToList();

        var topTitle = includeRuntime && string.IsNullOrWhiteSpace(functionFilter)
            ? "Top Functions (All)"
            : "Top Functions (Filtered)";
        var timeColumnLabel = string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase)
            ? "Samples"
            : $"Time ({timeUnitLabel})";
        var rows = new List<IReadOnlyList<string>>();

        foreach (var entry in filteredList.Take(15))
        {
            var funcName = FormatFunctionDisplayName(entry.Name);
            if (funcName.Length > 70) funcName = funcName[..67] + "...";

            var timeMs = entry.TimeMs;
            var calls = entry.Calls;
            var timeMsText = FormatCpuTime(timeMs, timeUnitLabel);
            var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
            var funcText = Markup.Escape(funcName);
            if (IsUnmanagedFrame(funcName))
            {
                funcText = $"[{_theme.RuntimeTypeColor}]{funcText}[/]";
            }

            rows.Add(new[]
            {
                funcText,
                $"[{_theme.CpuCountColor}]{callsText}[/]",
                $"[{_theme.CpuValueColor}]{timeMsText}[/]"
            });
        }

        var topTable = BuildTableWithRows(
            new[]
            {
                new TableColumnSpec("Function"),
                new TableColumnSpec(countLabel, RightAligned: true),
                new TableColumnSpec(timeColumnLabel, RightAligned: true)
            },
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
            PrintSection(topTitle, _theme.CpuCountColor);
            AnsiConsole.Write(topTable);
        }
        var filteredOut = allFunctions.Count - filteredList.Count;
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

        var totalTimeText = FormatCpuTime(totalTime, timeUnitLabel);
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

        PrintSection("Summary");
        WriteTable(
            new[]
            {
                new TableColumnSpec(string.Empty),
                new TableColumnSpec(string.Empty)
            },
            summaryRows,
            hideHeaders: true);

        AnsiConsole.Write(BuildCallTree(
            results,
            useSelfTime: false,
            resolvedRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoffPercent,
            timeUnitLabel,
            countSuffix,
            allocationTypeLimit,
            exceptionTypeLimit,
            hotThreshold,
            showTimeline,
            timelineWidth));
        if (showSelfTimeTree)
        {
            AnsiConsole.Write(BuildCallTree(
                results,
                useSelfTime: true,
                resolvedRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeRootMode,
                callTreeSiblingCutoffPercent,
                timeUnitLabel,
                countSuffix,
                allocationTypeLimit,
                exceptionTypeLimit,
                hotThreshold,
                showTimeline: false));
        }
    }

    public void PrintMemoryResults(
        MemoryProfileResult? results,
        string profileName,
        string? description,
        string? callTreeRoot,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        int callTreeSiblingCutoffPercent)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        PrintSection($"MEMORY PROFILE: {profileName}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            AnsiConsole.MarkupLine($"[dim]{description}[/]");
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
            WriteTable(
                new[]
                {
                    new TableColumnSpec("Metric"),
                    new TableColumnSpec("Value")
                },
                rows);
        }

        if (!string.IsNullOrWhiteSpace(results.AllocationByTypeRaw))
        {
            PrintSection("Allocation By Type (Sampled)", _theme.MemoryCountColor);
            AnsiConsole.WriteLine(results.AllocationByTypeRaw);
        }
        else if (results.AllocationEntries.Count > 0)
        {
            PrintSection("Allocation By Type (Sampled)", _theme.MemoryCountColor);
            PrintAllocationTable(results.AllocationEntries, results.AllocationTotal);
        }
        else if (!hasRows && !string.IsNullOrWhiteSpace(results.RawOutput))
        {
            AnsiConsole.WriteLine(results.RawOutput);
        }

        if (results.AllocationCallTree != null)
        {
            PrintSection("Allocation Call Tree (Sampled)");
            PrintAllocationCallTree(
                results.AllocationCallTree,
                callTreeRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeSiblingCutoffPercent);
        }
    }

    public void PrintExceptionResults(
        ExceptionProfileResult? results,
        string profileName,
        string? description,
        string? rootFilter,
        string? exceptionTypeFilter,
        string? functionFilter,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        string? callTreeRootMode,
        int callTreeSiblingCutoffPercent)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        PrintSection($"EXCEPTION PROFILE: {profileName}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            AnsiConsole.MarkupLine($"[dim]{description}[/]");
        }

        var selectedType = SelectExceptionType(results.ExceptionTypes, exceptionTypeFilter);
        var filteredExceptionTypes = FilterExceptionTypes(results.ExceptionTypes, exceptionTypeFilter);
        var hasTypeFilter = !string.IsNullOrWhiteSpace(exceptionTypeFilter);
        ExceptionTypeDetails? selectedDetails = null;
        if (!string.IsNullOrWhiteSpace(selectedType) &&
            results.TypeDetails.TryGetValue(selectedType, out var details))
        {
            selectedDetails = details;
        }

        if (hasTypeFilter && filteredExceptionTypes.Count == 0)
        {
            AnsiConsole.MarkupLine(
                $"[{_theme.AccentColor}]No exception types matched '{Markup.Escape(exceptionTypeFilter!)}'. Showing full results.[/]");
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
            PrintSection("Top Exceptions (Thrown)");
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

            WriteTable(
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

        PrintSection("Summary");
        WriteTable(
            new[]
            {
                new TableColumnSpec(string.Empty),
                new TableColumnSpec(string.Empty)
            },
            summaryRows,
            hideHeaders: true);

        if (summaryThrown > 0)
        {
            var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
            AnsiConsole.Write(BuildExceptionCallTree(
                selectedDetails?.ThrowRoot ?? results.ThrowCallTreeRoot,
                summaryThrown,
                "Call Tree (Thrown Exceptions)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                resolvedRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeRootMode,
                callTreeSiblingCutoffPercent));
        }

        var catchSites = selectedDetails?.CatchSites ?? results.CatchSites;
        var catchRoot = selectedDetails?.CatchRoot ?? results.CatchCallTreeRoot;
        if (summaryCaught > 0 && catchRoot != null)
        {
            var filteredCatchSites = catchSites.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
            if (!includeRuntime)
            {
                filteredCatchSites = filteredCatchSites.Where(entry => !IsRuntimeNoise(entry.Name));
            }
            var catchList = filteredCatchSites.ToList();
            if (catchList.Count > 0)
            {
                PrintSection("Top Catch Sites");
                var catchRows = new List<IReadOnlyList<string>>();

                foreach (var entry in catchList.Take(15))
                {
                    var funcName = FormatFunctionDisplayName(entry.Name);
                    if (funcName.Length > 70)
                    {
                        funcName = funcName[..67] + "...";
                    }

                    var countText = entry.Count.ToString("N0", CultureInfo.InvariantCulture);
                    var funcText = Markup.Escape(funcName);
                    if (IsUnmanagedFrame(funcName))
                    {
                        funcText = $"[{_theme.RuntimeTypeColor}]{funcText}[/]";
                    }

                    catchRows.Add(new[]
                    {
                        $"[{_theme.CpuCountColor}]{countText}[/]",
                        funcText
                    });
                }

                WriteTable(
                    new[]
                    {
                        new TableColumnSpec("Count", RightAligned: true),
                        new TableColumnSpec("Function")
                    },
                    catchRows);
            }

            var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
            AnsiConsole.Write(BuildExceptionCallTree(
                catchRoot,
                summaryCaught,
                "Call Tree (Catch Sites)",
                selectedType != null ? NameFormatter.FormatTypeDisplayName(selectedType) : null,
                resolvedRoot,
                includeRuntime,
                callTreeDepth,
                callTreeWidth,
                callTreeRootMode,
                callTreeSiblingCutoffPercent));
        }
    }

    public void PrintContentionResults(
        ContentionProfileResult? results,
        string profileName,
        string? description,
        string? rootFilter,
        string? functionFilter,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        string? callTreeRootMode,
        int callTreeSiblingCutoffPercent)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        PrintSection($"LOCK CONTENTION PROFILE: {profileName}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            AnsiConsole.MarkupLine($"[dim]{description}[/]");
        }

        var filteredAll = results.TopFunctions.Where(entry => MatchesFunctionFilter(entry.Name, functionFilter));
        if (!includeRuntime)
        {
            filteredAll = filteredAll.Where(entry => !IsRuntimeNoise(entry.Name));
        }
        var filteredList = filteredAll.ToList();

        var topTitle = includeRuntime && string.IsNullOrWhiteSpace(functionFilter)
            ? "Top Contended Functions (All)"
            : "Top Contended Functions (Filtered)";
        var rows = new List<IReadOnlyList<string>>();
        PrintSection(topTitle);

        foreach (var entry in filteredList.Take(15))
        {
            var funcName = FormatFunctionDisplayName(entry.Name);
            if (funcName.Length > 70)
            {
                funcName = funcName[..67] + "...";
            }

            var waitText = entry.TimeMs.ToString("F2", CultureInfo.InvariantCulture);
            var countText = entry.Calls.ToString("N0", CultureInfo.InvariantCulture);
            var funcText = Markup.Escape(funcName);
            if (IsUnmanagedFrame(funcName))
            {
                funcText = $"[{_theme.RuntimeTypeColor}]{funcText}[/]";
            }
            rows.Add(new[]
            {
                $"[{_theme.CpuValueColor}]{waitText}[/]",
                $"[{_theme.CpuCountColor}]{countText}[/]",
                funcText
            });
        }

        WriteTable(
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
            AnsiConsole.MarkupLine(
                $"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
        }
        if (!string.IsNullOrWhiteSpace(functionFilter))
        {
            AnsiConsole.MarkupLine(
                $"[dim]Filter: {Markup.Escape(functionFilter)} (use --filter to change).[/]");
        }

        var totalWaitText = results.TotalWaitMs.ToString("F2", CultureInfo.InvariantCulture);
        var totalCountText = results.TotalCount.ToString("N0", CultureInfo.InvariantCulture);
        var summaryRows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "[bold]Total Wait[/]",
                $"[{_theme.CpuValueColor}]{totalWaitText} ms[/]"
            },
            new[]
            {
                "[bold]Total Contentions[/]",
                $"[{_theme.CpuCountColor}]{totalCountText}[/]"
            }
        };

        PrintSection("Summary");
        WriteTable(
            new[]
            {
                new TableColumnSpec(string.Empty),
                new TableColumnSpec(string.Empty)
            },
            summaryRows,
            hideHeaders: true);

        var resolvedRoot = ResolveCallTreeRootFilter(rootFilter);
        AnsiConsole.Write(BuildContentionCallTree(
            results,
            resolvedRoot,
            includeRuntime,
            callTreeDepth,
            callTreeWidth,
            callTreeRootMode,
            callTreeSiblingCutoffPercent));
    }

    public void PrintHeapResults(HeapProfileResult? results, string profileName, string? description)
    {
        if (results == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No results to display[/]");
            return;
        }

        PrintSection($"HEAP SNAPSHOT: {profileName}");
        if (!string.IsNullOrWhiteSpace(description))
        {
            AnsiConsole.MarkupLine($"[dim]{description}[/]");
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

            WriteTable(
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

    private void PrintAllocationCallTree(
        AllocationCallTreeResult callTree,
        string? callTreeRoot,
        bool includeRuntime,
        int callTreeDepth,
        int callTreeWidth,
        int callTreeSiblingCutoffPercent)
    {
        var rootLabel = callTree.TypeRoots.Count > 0
            ? NameFormatter.FormatTypeDisplayName(callTree.TypeRoots[0].Name)
            : "Allocations";
        var rootNode = callTree.TypeRoots.Count > 0
            ? callTree.TypeRoots[0]
            : new AllocationCallTreeNode(rootLabel);
        if (!string.IsNullOrWhiteSpace(callTreeRoot))
        {
            var matchingRoots = callTree.TypeRoots
                .Where(root => root.Name.Contains(callTreeRoot, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingRoots.Count > 0)
            {
                rootNode = matchingRoots[0];
            }
        }

        var tree = BuildAllocationCallTree(rootNode, includeRuntime, callTreeDepth, callTreeWidth, callTreeSiblingCutoffPercent);
        AnsiConsole.Write(tree);
    }

    private void PrintSection(string text, string? color = null)
    {
        Console.WriteLine();
        if (string.IsNullOrWhiteSpace(color))
        {
            Console.WriteLine(text);
            return;
        }

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(text)}[/]");
    }

    private Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Color.Default;
        }

        var value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new Color(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    private bool TryParseHexColor(string value, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(trimmed.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(trimmed.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(trimmed.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = (r, g, b);
        return true;
    }

    private string InterpolateColor((byte R, byte G, byte B) start, (byte R, byte G, byte B) end, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        var r = (byte)Math.Round(start.R + (end.R - start.R) * t);
        var g = (byte)Math.Round(start.G + (end.G - start.G) * t);
        var b = (byte)Math.Round(start.B + (end.B - start.B) * t);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private string? GetHotnessColor(double hotness)
    {
        if (!TryParseHexColor(_theme.TextColor, out var cool) ||
            !TryParseHexColor(_theme.HotColor, out var hot))
        {
            return null;
        }

        double normalizedHotness;
        if (hotness <= HotnessColorFloor)
        {
            normalizedHotness = 0d;
        }
        else if (hotness <= HotnessColorMid)
        {
            var span = HotnessColorMid - HotnessColorFloor;
            normalizedHotness = span > 0d
                ? (hotness - HotnessColorFloor) / span * 0.5d
                : 0d;
        }
        else if (hotness >= HotnessColorMax)
        {
            normalizedHotness = 1d;
        }
        else
        {
            var span = HotnessColorMax - HotnessColorMid;
            normalizedHotness = span > 0d
                ? 0.5d + (hotness - HotnessColorMid) / span * 0.5d
                : 1d;
        }

        normalizedHotness = Math.Clamp(normalizedHotness, 0d, 1d);
        return InterpolateColor(cool, hot, normalizedHotness);
    }

    private Table BuildTable(
        IReadOnlyList<TableColumnSpec> columns,
        string? title = null,
        bool hideHeaders = false,
        string? headerColor = null)
    {
        var table = new Table();
        table.Expand = false;
        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Grey));
        table.ShowHeaders = !hideHeaders;
        table.ShowRowSeparators = false;
        table.Title = title != null
            ? new TableTitle(new Markup(title))
            : null;

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

    private Table BuildTableWithRows(
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

    private void WriteTable(
        IReadOnlyList<TableColumnSpec> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        string? title = null,
        bool hideHeaders = false,
        string? headerColor = null)
    {
        AnsiConsole.Write(BuildTableWithRows(columns, rows, title, hideHeaders, headerColor));
    }

    private IRenderable BuildTableBlock(Table table, string title, string color)
    {
        return new Rows(
            new Markup($"[{color}]{Markup.Escape(title)}[/]"),
            table);
    }

    private IRenderable BuildContentionCallTree(
        ContentionProfileResult results,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent)
    {
        var callTreeRoot = results.CallTreeRoot;
        var totalTime = results.TotalWaitMs;
        var totalSamples = callTreeRoot.Calls;
        var title = "Call Tree (Wait Time)";
        maxDepth = Math.Max(1, maxDepth);
        maxWidth = Math.Max(1, maxWidth);
        siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

        var rootNode = callTreeRoot;
        var rootTotal = totalTime;
        if (!string.IsNullOrWhiteSpace(rootFilter))
        {
            var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
            if (matches.Count > 0)
            {
                rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
                rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
                title = $"{title} - root: {Markup.Escape(rootFilter)}";
            }
            else
            {
                AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
            }
        }

        var rootLabel = FormatCallTreeLine(
            rootNode,
            rootTotal,
            totalSamples,
            useSelfTime: false,
            isRoot: true,
            timeUnitLabel: "ms",
            countSuffix: "x");
        var tree = new Tree(rootLabel)
        {
            Style = _treeGuideStyle,
            Guide = new CompactTreeGuide()
        };
        var children = CallTreeFilters.GetVisibleChildren(
            rootNode,
            includeRuntime,
            useSelfTime: false,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                         CallTreeFilters.GetVisibleChildren(
                             child,
                             includeRuntime,
                             useSelfTime: false,
                             maxWidth,
                             siblingCutoffPercent,
                             IsRuntimeNoise).Count == 0;
            var childNode = tree.AddNode(FormatCallTreeLine(
                child,
                rootTotal,
                totalSamples,
                useSelfTime: false,
                isRoot: false,
                timeUnitLabel: "ms",
                countSuffix: "x",
                isLeaf: isLeaf));
            if (!isSpecialLeaf)
            {
                AddCallTreeChildren(
                    childNode,
                    child,
                    rootTotal,
                    totalSamples,
                    useSelfTime: false,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent,
                    "ms",
                    "x",
                    0,
                    0,
                    HotnessFireThreshold,
                    highlightHotspots: false);
            }
        }

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private IRenderable BuildExceptionCallTree(
        CallTreeNode callTreeRoot,
        long totalCount,
        string title,
        string? rootLabelOverride,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent)
    {
        maxDepth = Math.Max(1, maxDepth);
        maxWidth = Math.Max(1, maxWidth);
        siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

        var rootNode = callTreeRoot;
        var rootTotal = (double)totalCount;
        if (!string.IsNullOrWhiteSpace(rootFilter))
        {
            var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
            if (matches.Count > 0)
            {
                rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
                rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
                title = $"{title} - root: {Markup.Escape(rootFilter)}";
            }
            else
            {
                AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
            }
        }

        var rootLabel = FormatExceptionCallTreeLine(rootNode, rootTotal, isRoot: true, rootLabelOverride);
        var tree = new Tree(rootLabel)
        {
            Style = _treeGuideStyle,
            Guide = new CompactTreeGuide()
        };
        var children = CallTreeFilters.GetVisibleChildren(
            rootNode,
            includeRuntime,
            useSelfTime: false,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                         CallTreeFilters.GetVisibleChildren(
                             child,
                             includeRuntime,
                             useSelfTime: false,
                             maxWidth,
                             siblingCutoffPercent,
                             IsRuntimeNoise).Count == 0;
            var childNode = tree.AddNode(FormatExceptionCallTreeLine(child, rootTotal, isRoot: false, rootLabelOverride: null, isLeaf));
            if (!isSpecialLeaf)
            {
                AddExceptionCallTreeChildren(
                    childNode,
                    child,
                    rootTotal,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private IRenderable BuildAllocationCallTree(
        AllocationCallTreeNode root,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent)
    {
        var rootLabel = FormatAllocationCallTreeLine(root, root.TotalBytes, isRoot: true, isLeaf: false);
        var tree = new Tree(rootLabel)
        {
            Style = _treeGuideStyle,
            Guide = new CompactTreeGuide()
        };
        var children = GetVisibleAllocationChildren(root, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
            var childChildren = !isSpecialLeaf
                ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
                : Array.Empty<AllocationCallTreeNode>();
            var isLeaf = isSpecialLeaf || maxDepth <= 1 || childChildren.Count == 0;

            var childNode = tree.AddNode(FormatAllocationCallTreeLine(child, root.TotalBytes, isRoot: false, isLeaf));
            if (!isSpecialLeaf)
            {
                AddAllocationCallTreeChildren(
                    childNode,
                    child,
                    root.TotalBytes,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }

        return tree;
    }

    private void AddAllocationCallTreeChildren(
        TreeNode parent,
        AllocationCallTreeNode node,
        long rootTotalBytes,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var children = GetVisibleAllocationChildren(node, includeRuntime, maxWidth, siblingCutoffPercent);
        foreach (var child in children)
        {
            var nextDepth = depth + 1;
            var isSpecialLeaf = ShouldStopAtLeaf(FormatFunctionDisplayName(child.Name));
            var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
                ? GetVisibleAllocationChildren(child, includeRuntime, maxWidth, siblingCutoffPercent)
                : Array.Empty<AllocationCallTreeNode>();
            var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;

            var childNode = parent.AddNode(FormatAllocationCallTreeLine(child, rootTotalBytes, isRoot: false, isLeaf));
            if (!isSpecialLeaf)
            {
                AddAllocationCallTreeChildren(
                    childNode,
                    child,
                    rootTotalBytes,
                    includeRuntime,
                    nextDepth,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }
    }

    private IReadOnlyList<AllocationCallTreeNode> GetVisibleAllocationChildren(
        AllocationCallTreeNode node,
        bool includeRuntime,
        int maxWidth,
        int siblingCutoffPercent)
    {
        var ordered = EnumerateVisibleAllocationChildren(node, includeRuntime)
            .OrderByDescending(child => child.TotalBytes)
            .ToList();

        if (ordered.Count == 0)
        {
            return ordered;
        }

        if (siblingCutoffPercent <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var topBytes = ordered[0].TotalBytes;
        if (topBytes <= 0)
        {
            return ordered.Take(maxWidth).ToList();
        }

        var minBytes = topBytes * siblingCutoffPercent / 100d;
        return ordered
            .Where(child => child.TotalBytes >= minBytes)
            .Take(maxWidth)
            .ToList();
    }

    private IEnumerable<AllocationCallTreeNode> EnumerateVisibleAllocationChildren(
        AllocationCallTreeNode node,
        bool includeRuntime)
    {
        foreach (var child in node.Children.Values)
        {
            if (includeRuntime || !IsRuntimeNoise(child.Name))
            {
                yield return child;
                continue;
            }

            foreach (var grandChild in EnumerateVisibleAllocationChildren(child, includeRuntime))
            {
                yield return grandChild;
            }
        }
    }

    private string FormatAllocationCallTreeLine(
        AllocationCallTreeNode node,
        long rootTotalBytes,
        bool isRoot,
        bool isLeaf)
    {
        var bytes = node.TotalBytes;
        var pct = rootTotalBytes > 0 ? 100d * bytes / rootTotalBytes : 0d;
        var count = node.Count;
        var bytesText = FormatBytes(bytes);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var countText = count.ToString("N0", CultureInfo.InvariantCulture);

        var displayName = isRoot ? NameFormatter.FormatTypeDisplayName(node.Name) : FormatFunctionDisplayName(node.Name);
        if (displayName.Length > 80)
        {
            displayName = displayName[..77] + "...";
        }

        var nameText = isRoot
            ? $"[{_theme.TextColor}]{Markup.Escape(displayName)}[/]"
            : FormatCallTreeName(displayName, displayName, isLeaf);

        return $"[{_theme.CpuValueColor}]{bytesText}[/] [{_theme.SampleColor}]{pctText}%[/] [{_theme.CpuCountColor}]{countText}x[/] {nameText}";
    }

    private IRenderable BuildCallTree(
        CpuProfileResult results,
        bool useSelfTime,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool showTimeline = false,
        int timelineWidth = 40)
    {
        var callTreeRoot = results.CallTreeRoot;
        var totalTime = results.CallTreeTotal;
        var totalSamples = callTreeRoot.Calls;
        var title = useSelfTime ? "Call Tree (Self Time)" : "Call Tree (Total Time)";
        maxDepth = Math.Max(1, maxDepth);
        maxWidth = Math.Max(1, maxWidth);
        siblingCutoffPercent = Math.Max(0, siblingCutoffPercent);

        var rootNode = callTreeRoot;
        var rootTotal = totalTime;
        if (!string.IsNullOrWhiteSpace(rootFilter))
        {
            var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
            if (matches.Count > 0)
            {
                rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
                rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
                title = $"{title} - root: {Markup.Escape(rootFilter)}";
            }
            else
            {
                AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
            }
        }

        if (showTimeline && rootNode.HasTiming)
        {
            var terminalWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 160;

            var actualTimelineWidth = Math.Max(20, timelineWidth);
            var treeColumnWidth = terminalWidth - actualTimelineWidth - 2;

            var timeline = new TimelineContext
            {
                RootStart = rootNode.MinStart,
                RootEnd = rootNode.MaxEnd,
                BarWidth = actualTimelineWidth,
                TextWidth = treeColumnWidth,
                MaxNameLength = 200,
                MaxDepth = maxDepth
            };

            var rows = new List<(string TreeText, int VisibleLength, string TimelineBar)>();
            CollectTimelineRows(
                rows,
                rootNode,
                rootTotal,
                totalSamples,
                useSelfTime,
                timeUnitLabel,
                countSuffix,
                "",
                true,
                isHotspot: false,
                highlightHotspots: true,
                includeRuntime,
                0,
                maxDepth,
                maxWidth,
                siblingCutoffPercent,
                hotThreshold,
                timeline);

            var outputLines = new List<IRenderable> { new Markup($"[bold {_theme.AccentColor}]{title}[/]") };
            foreach (var (treeText, visibleLength, timelineBar) in rows)
            {
                var padding = Math.Max(0, treeColumnWidth - visibleLength);
                outputLines.Add(new Markup($"{treeText}{new string(' ', padding)}{timelineBar}"));
            }

            return new Rows(outputLines);
        }

        var rootLabel = FormatCallTreeLine(
            rootNode,
            rootTotal,
            totalSamples,
            useSelfTime,
            isRoot: true,
            timeUnitLabel: timeUnitLabel,
            countSuffix: countSuffix,
            isLeaf: false,
            timeline: null,
            depth: 0,
            useHeatColor: true);
        var tree = new Tree(rootLabel)
        {
            Style = _treeGuideStyle,
            Guide = new CompactTreeGuide()
        };
        if (allocationTypeLimit > 0)
        {
            AddAllocationTypeNodes(tree, rootNode, allocationTypeLimit);
        }
        if (exceptionTypeLimit > 0)
        {
            AddExceptionTypeNodes(tree, rootNode, exceptionTypeLimit);
        }
        var children = CallTreeFilters.GetVisibleChildren(
            rootNode,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        foreach (var child in children)
        {
            var childHotness = ComputeHotness(child, rootTotal, totalSamples);
            var isHotspot = IsFireEmojiCandidate(childHotness, hotThreshold);
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var isLeaf = isSpecialLeaf || maxDepth <= 1 ||
                         CallTreeFilters.GetVisibleChildren(
                             child,
                             includeRuntime,
                             useSelfTime,
                             maxWidth,
                             siblingCutoffPercent,
                             IsRuntimeNoise).Count == 0;
            var childNode = tree.AddNode(FormatCallTreeLine(
                child,
                rootTotal,
                totalSamples,
                useSelfTime,
                isRoot: false,
                timeUnitLabel: timeUnitLabel,
                countSuffix: countSuffix,
                isLeaf: isLeaf,
                timeline: null,
                depth: 1,
                isHotspot: isHotspot,
                useHeatColor: true));
            if (allocationTypeLimit > 0)
            {
                AddAllocationTypeNodes(childNode, child, allocationTypeLimit);
            }
            if (exceptionTypeLimit > 0)
            {
                AddExceptionTypeNodes(childNode, child, exceptionTypeLimit);
            }
            if (!isSpecialLeaf)
            {
                AddCallTreeChildren(
                    childNode,
                    child,
                    rootTotal,
                    totalSamples,
                    useSelfTime,
                    includeRuntime,
                    2,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent,
                    timeUnitLabel,
                    countSuffix,
                    allocationTypeLimit,
                    exceptionTypeLimit,
                    hotThreshold,
                    highlightHotspots: true,
                    timeline: null);
            }
        }

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private void CollectTimelineRows(
        List<(string TreeText, int VisibleLength, string TimelineBar)> rows,
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        string timeUnitLabel,
        string countSuffix,
        string prefix,
        bool isRoot,
        bool isHotspot,
        bool highlightHotspots,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent,
        double hotThreshold,
        TimelineContext timeline,
        string? continuationPrefix = null)
    {
        var (treeText, visibleLength) = FormatCallTreeLineSimple(
            node,
            totalTime,
            totalSamples,
            useSelfTime,
            isRoot,
            timeUnitLabel,
            countSuffix,
            prefix,
            timeline.TextWidth,
            isHotspot,
            useHeatColor: true);
        var timelineBar = RenderTimelineBar(node, timeline);
        rows.Add((treeText, visibleLength, timelineBar));

        if (depth >= maxDepth)
        {
            return;
        }

        var basePrefix = continuationPrefix ?? prefix;

        var children = CallTreeFilters.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var isLast = i == children.Count - 1;
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var childHotness = ComputeHotness(child, totalTime, totalSamples);
            var isChildHotspot = highlightHotspots && IsFireEmojiCandidate(childHotness, hotThreshold);

            var connector = isLast ? "└─ " : "├─ ";
            var continuation = isLast ? "   " : "│  ";

            CollectTimelineRows(
                rows,
                child,
                totalTime,
                totalSamples,
                useSelfTime,
                timeUnitLabel,
                countSuffix,
                basePrefix + connector,
                isRoot: false,
                isHotspot: isChildHotspot,
                highlightHotspots: highlightHotspots,
                includeRuntime,
                depth + 1,
                isSpecialLeaf ? depth + 1 : maxDepth,
                maxWidth,
                siblingCutoffPercent,
                hotThreshold,
                timeline,
                basePrefix + continuation);
        }
    }

    private (string Text, int VisibleLength) FormatCallTreeLineSimple(
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool isRoot,
        string timeUnitLabel,
        string countSuffix,
        string prefix,
        int maxWidth,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var matchName = GetCallTreeMatchName(node);
        var displayName = GetCallTreeDisplayName(matchName);

        if (isHotspot)
        {
            displayName = $"{HotspotMarker} {displayName}";
        }

        var timeSpent = isRoot && useSelfTime
            ? GetCallTreeTime(node, useSelfTime: false)
            : GetCallTreeTime(node, useSelfTime);
        var calls = node.Calls;
        var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
        var timeText = FormatCpuTime(timeSpent, timeUnitLabel);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
        var countText = callsText + countSuffix;

        var statsText = $"{timeText} {timeUnitLabel} {pctText}% {countText} ";
        var statsLength = prefix.Length + statsText.Length;
        var maxNameLength = maxWidth - statsLength - 1;

        var truncatedName = displayName;
        if (maxNameLength > 3 && displayName.Length > maxNameLength)
        {
            truncatedName = displayName[..(maxNameLength - 3)] + "...";
        }
        else if (maxNameLength <= 3)
        {
            truncatedName = "...";
        }

        var hotness = ComputeHotness(node, totalTime, totalSamples);
        var nameColor = useHeatColor ? GetHotnessColor(hotness) : null;
        var nameText = FormatCallTreeName(truncatedName, matchName, ShouldStopAtLeaf(matchName), nameColor);

        var visibleLength = statsLength + truncatedName.Length;

        return ($"[dim]{Markup.Escape(prefix)}[/]" +
                $"[{_theme.CpuValueColor}]{timeText} {timeUnitLabel}[/] " +
                $"[{_theme.SampleColor}]{pctText}%[/] " +
                $"[{_theme.CpuCountColor}]{countText}[/] {nameText}", visibleLength);
    }

    private void AddCallTreeChildren(
        TreeNode parent,
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool highlightHotspots = false,
        TimelineContext? timeline = null)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var children = CallTreeFilters.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        foreach (var child in children)
        {
            var nextDepth = depth + 1;
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var childChildren = !isSpecialLeaf && nextDepth <= maxDepth
                ? CallTreeFilters.GetVisibleChildren(
                    child,
                    includeRuntime,
                    useSelfTime,
                    maxWidth,
                    siblingCutoffPercent,
                    IsRuntimeNoise)
                : Array.Empty<CallTreeNode>();
            var isLeaf = isSpecialLeaf || nextDepth > maxDepth || childChildren.Count == 0;
            var childHotness = ComputeHotness(child, totalTime, totalSamples);
            var isHotspot = highlightHotspots && IsFireEmojiCandidate(childHotness, hotThreshold);

            var childNode = parent.AddNode(FormatCallTreeLine(
                child,
                totalTime,
                totalSamples,
                useSelfTime,
                isRoot: false,
                timeUnitLabel: timeUnitLabel,
                countSuffix: countSuffix,
                isLeaf: isLeaf,
                timeline: timeline,
                depth: depth,
                isHotspot: isHotspot,
                useHeatColor: highlightHotspots));
            if (allocationTypeLimit > 0)
            {
                AddAllocationTypeNodes(childNode, child, allocationTypeLimit);
            }
            if (exceptionTypeLimit > 0)
            {
                AddExceptionTypeNodes(childNode, child, exceptionTypeLimit);
            }
            if (!isSpecialLeaf)
            {
                AddCallTreeChildren(
                    childNode,
                    child,
                    totalTime,
                    totalSamples,
                    useSelfTime,
                    includeRuntime,
                    depth + 1,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent,
                    timeUnitLabel,
                    countSuffix,
                    allocationTypeLimit,
                    exceptionTypeLimit,
                    hotThreshold,
                    highlightHotspots,
                    timeline);
            }
        }
    }

    private void AddAllocationTypeNodes(IHasTreeNodes parent, CallTreeNode node, int limit)
    {
        if (limit <= 0 || node.AllocationByType == null || node.AllocationByType.Count == 0)
        {
            return;
        }

        foreach (var entry in node.AllocationByType.OrderByDescending(kv => kv.Value).Take(limit))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Key);
            var bytesText = FormatBytes(entry.Value);
            var count = node.AllocationCountByType != null &&
                        node.AllocationCountByType.TryGetValue(entry.Key, out var allocationCount)
                ? allocationCount
                : 0;
            var countText = count > 0
                ? count.ToString("N0", CultureInfo.InvariantCulture) + "x"
                : "0x";
            var line = $"[{_theme.MemoryValueColor}]{bytesText}[/] [{_theme.MemoryCountColor}]{countText}[/] {Markup.Escape(typeName)}";
            parent.AddNode(line);
        }
    }

    private void AddExceptionTypeNodes(IHasTreeNodes parent, CallTreeNode node, int limit)
    {
        if (limit <= 0 || node.ExceptionByType == null || node.ExceptionByType.Count == 0)
        {
            return;
        }

        foreach (var entry in node.ExceptionByType.OrderByDescending(kv => kv.Value).Take(limit))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Key);
            var countText = entry.Value.ToString("N0", CultureInfo.InvariantCulture) + "x";
            var line = $"[{_theme.ErrorColor}]{countText}[/] {Markup.Escape(typeName)}";
            parent.AddNode(line);
        }
    }

    private void AddExceptionCallTreeChildren(
        TreeNode parent,
        CallTreeNode node,
        double totalCount,
        bool includeRuntime,
        int depth,
        int maxDepth,
        int maxWidth,
        int siblingCutoffPercent)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var children = CallTreeFilters.GetVisibleChildren(
            node,
            includeRuntime,
            useSelfTime: false,
            maxWidth,
            siblingCutoffPercent,
            IsRuntimeNoise);
        foreach (var child in children)
        {
            var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
            var isLeaf = isSpecialLeaf || depth + 1 > maxDepth ||
                         CallTreeFilters.GetVisibleChildren(
                             child,
                             includeRuntime,
                             useSelfTime: false,
                             maxWidth,
                             siblingCutoffPercent,
                             IsRuntimeNoise).Count == 0;
            var childNode = parent.AddNode(FormatExceptionCallTreeLine(child, totalCount, isRoot: false, rootLabelOverride: null, isLeaf));
            if (!isSpecialLeaf)
            {
                AddExceptionCallTreeChildren(
                    childNode,
                    child,
                    totalCount,
                    includeRuntime,
                    depth + 1,
                    maxDepth,
                    maxWidth,
                    siblingCutoffPercent);
            }
        }
    }

    private string FormatCallTreeLine(
        CallTreeNode node,
        double totalTime,
        double totalSamples,
        bool useSelfTime,
        bool isRoot,
        string timeUnitLabel,
        string countSuffix,
        bool isLeaf = false,
        TimelineContext? timeline = null,
        int depth = 0,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var matchName = GetCallTreeMatchName(node);
        var displayName = GetCallTreeDisplayName(matchName);

        if (isHotspot)
        {
            displayName = $"{HotspotMarker} {displayName}";
        }

        var timeSpent = isRoot && useSelfTime
            ? GetCallTreeTime(node, useSelfTime: false)
            : GetCallTreeTime(node, useSelfTime);
        var calls = node.Calls;
        var pct = totalTime > 0 ? 100 * timeSpent / totalTime : 0;
        var timeText = FormatCpuTime(timeSpent, timeUnitLabel);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var callsText = calls.ToString("N0", CultureInfo.InvariantCulture);
        var countText = callsText + countSuffix;
        var hotness = ComputeHotness(node, totalTime, totalSamples);
        var nameColor = useHeatColor ? GetHotnessColor(hotness) : null;

        int maxNameLen;
        if (timeline != null)
        {
            var treeGuideWidth = depth * 4;
            var statsText = $"{timeText} {timeUnitLabel} {pctText}% {countText} ";
            maxNameLen = Math.Max(15, timeline.TextWidth - treeGuideWidth - statsText.Length);
        }
        else
        {
            maxNameLen = 80;
        }

        if (displayName.Length > maxNameLen)
        {
            displayName = displayName[..(maxNameLen - 3)] + "...";
        }

        var nameText = FormatCallTreeName(displayName, matchName, isLeaf, nameColor);

        var baseLine =
            $"[{_theme.CpuValueColor}]{timeText} {timeUnitLabel}[/] " +
            $"[{_theme.SampleColor}]{pctText}%[/] " +
            $"[{_theme.CpuCountColor}]{countText}[/] {nameText}";

        if (timeline != null && node.HasTiming)
        {
            var bar = RenderTimelineBar(node, timeline);
            var visibleLength = EstimateVisibleLength(baseLine);
            var padding = timeline.GetPaddingForDepth(depth, visibleLength);
            var paddedLine = baseLine + new string(' ', padding);
            return $"{paddedLine} [dim]│[/] {bar}";
        }

        return baseLine;
    }

    private int EstimateVisibleLength(string markup)
    {
        var result = markup;
        while (true)
        {
            var start = result.IndexOf('[');
            if (start < 0) break;
            var end = result.IndexOf(']', start);
            if (end < 0) break;
            result = result.Remove(start, end - start + 1);
        }
        return result.Length;
    }

    private string RenderTimelineBar(CallTreeNode node, TimelineContext ctx)
    {
        if (!node.HasTiming || ctx.RootDuration <= 0)
        {
            return new string(' ', ctx.BarWidth);
        }

        var buffer = new char[ctx.BarWidth];
        Array.Fill(buffer, ' ');

        var startOffset = node.MinStart - ctx.RootStart;
        var startRatio = Math.Clamp(startOffset / ctx.RootDuration, 0, 1);
        var durationRatio = Math.Clamp((node.MaxEnd - node.MinStart) / ctx.RootDuration, 0, 1);

        var scaledWidth = ctx.BarWidth * 8;
        var startUnit = (int)Math.Round(startRatio * scaledWidth);
        var durationUnits = Math.Max(1, (int)Math.Round(durationRatio * scaledWidth));
        var endUnit = Math.Min(startUnit + durationUnits, scaledWidth);

        for (var column = 0; column < ctx.BarWidth; column++)
        {
            var columnStart = column * 8;
            var columnEnd = columnStart + 8;
            var overlap = Math.Max(0, Math.Min(columnEnd, endUnit) - Math.Max(columnStart, startUnit));
            if (overlap <= 0)
            {
                continue;
            }

            var includesStart = startUnit >= columnStart && startUnit < columnEnd;
            var includesEnd = endUnit > columnStart && endUnit <= columnEnd;

            buffer[column] = overlap switch
            {
                >= 8 => '█',
                _ when includesStart && !includesEnd => SelectRightBlock(overlap / 8.0),
                _ when includesEnd && !includesStart => SelectLeftBlock(overlap / 8.0),
                _ when includesStart && includesEnd => SelectLeftBlock(overlap / 8.0),
                _ => SelectLeftBlock(overlap / 8.0)
            };
        }

        var pct = durationRatio * 100;
        var color = GetHeatColor(pct);

        return $"[{color}]{new string(buffer)}[/]";
    }

    private string GetHeatColor(double percentage)
    {
        percentage = Math.Clamp(percentage, 0, 100);

        int r, g, b;

        if (percentage <= 5)
        {
            r = 0;
            g = 200;
            b = 0;
        }
        else if (percentage <= 50)
        {
            var t = (percentage - 5) / 45.0;
            r = (int)(0 + t * 255);
            g = (int)(200 - t * 35);
            b = 0;
        }
        else
        {
            var t = (percentage - 50) / 50.0;
            r = 255;
            g = (int)(165 - t * 165);
            b = 0;
        }

        return $"rgb({r},{g},{b})";
    }

    private char SelectLeftBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.875 => '▉',
            >= 0.75 => '▊',
            >= 0.625 => '▋',
            >= 0.5 => '▌',
            >= 0.375 => '▍',
            >= 0.25 => '▎',
            >= 0.125 => '▏',
            _ => ' '
        };
    }

    private char SelectRightBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.5 => '▐',
            >= 0.125 => '▕',
            _ => ' '
        };
    }

    private string FormatExceptionCallTreeLine(
        CallTreeNode node,
        double totalCount,
        bool isRoot,
        string? rootLabelOverride,
        bool isLeaf = false)
    {
        var matchName = GetCallTreeMatchName(node);
        var displayName = isRoot && !string.IsNullOrWhiteSpace(rootLabelOverride)
            ? rootLabelOverride
            : GetCallTreeDisplayName(matchName);
        if (displayName.Length > 80)
        {
            displayName = displayName[..77] + "...";
        }

        var count = isRoot ? totalCount : node.Total;
        var pct = totalCount > 0 ? 100 * count / totalCount : 0;
        var countText = count.ToString("N0", CultureInfo.InvariantCulture);
        var pctText = pct.ToString("F1", CultureInfo.InvariantCulture);
        var nameText = FormatCallTreeName(displayName, matchName, isLeaf);

        return $"[{_theme.CpuValueColor}]{countText}x[/] [{_theme.SampleColor}]{pctText}%[/] {nameText}";
    }

    private double GetCallTreeTime(CallTreeNode node, bool useSelfTime) => useSelfTime ? node.Self : node.Total;

    private double ComputeHotness(CallTreeNode node, double totalTime, double totalSamples)
    {
        if (totalTime <= 0 || totalSamples <= 0)
        {
            return 0;
        }

        var sampleRatio = node.Calls / totalSamples;
        var selfRatio = node.Self / totalTime;
        return sampleRatio * selfRatio;
    }

    private bool IsFireEmojiCandidate(double hotness, double hotThreshold) => hotness >= hotThreshold;

    private List<CallTreeMatch> FindCallTreeMatches(CallTreeNode node, string filter)
    {
        var matches = new List<CallTreeMatch>();
        var normalizedFilter = filter.Trim();
        if (normalizedFilter.Length == 0)
        {
            return matches;
        }

        var order = 0;
        void Visit(CallTreeNode current, int depth)
        {
            if (current.FrameIdx >= 0)
            {
                var displayName = FormatFunctionDisplayName(current.Name);
                if (displayName.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase) ||
                    current.Name.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new CallTreeMatch(current, depth, order++));
                }
            }

            foreach (var child in current.Children.Values)
            {
                Visit(child, depth + 1);
            }
        }

        Visit(node, 0);
        return matches;
    }

    private string? ResolveCallTreeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }

    private CallTreeNode SelectRootMatch(List<CallTreeMatch> matches, bool includeRuntime, string? rootMode)
    {
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("No call tree matches available.");
        }

        var mode = NormalizeRootMode(rootMode);
        var candidates = includeRuntime
            ? matches
            : matches.Where(match => !IsRuntimeNoise(match.Node.Name)).ToList();
        if (candidates.Count == 0)
        {
            candidates = matches;
        }

        return mode switch
        {
            "first" or "shallowest" => candidates
                .OrderBy(match => match.Depth)
                .ThenBy(match => match.Order)
                .Select(match => match.Node)
                .First(),
            _ => candidates
                .OrderByDescending(match => GetCallTreeTime(match.Node, useSelfTime: false))
                .Select(match => match.Node)
                .First()
        };
    }

    private string NormalizeRootMode(string? rootMode)
    {
        if (string.IsNullOrWhiteSpace(rootMode))
        {
            return "hottest";
        }

        return rootMode.Trim().ToLowerInvariant();
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
            var count = entry.Count;
            var totalText = entry.Total ?? string.Empty;
            var paddedTotalText = totalText.Length == 0 ? totalText : " " + totalText;

            totalCount += count;

            var countText = count.ToString("N0", CultureInfo.InvariantCulture);
            rows.Add(new[]
            {
                $"[{_theme.TextColor}]{Markup.Escape(typeName)}[/]",
                $"[{_theme.MemoryCountColor}]{Markup.Escape(countText)}[/]",
                $"[{_theme.MemoryValueColor}]{Markup.Escape(paddedTotalText)}[/]"
            });
        }

        if (!string.IsNullOrWhiteSpace(allocationTotal))
        {
            var countText = totalCount.ToString("N0", CultureInfo.InvariantCulture);
            var paddedAllocationTotal = " " + allocationTotal;
            rows.Add(new[]
            {
                $"[bold {_theme.TextColor}]TOTAL (shown)[/]",
                $"[bold {_theme.MemoryCountColor}]{Markup.Escape(countText)}[/]",
                $"[bold {_theme.MemoryValueColor}]{Markup.Escape(paddedAllocationTotal)}[/]"
            });
        }

        return BuildTableWithRows(
            new[]
            {
                new TableColumnSpec("Type"),
                new TableColumnSpec("Count", RightAligned: true),
                new TableColumnSpec(" Total", RightAligned: true)
            },
            rows);
    }

    private void PrintAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
    {
        var table = BuildAllocationTable(entries, allocationTotal);
        if (table == null)
        {
            return;
        }

        AnsiConsole.Write(table);
    }

    private string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024d).ToString("F2", CultureInfo.InvariantCulture) + " KB";
        }

        if (bytes < 1024L * 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / (1024d * 1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " GB";
    }

    private string FormatCpuTime(double value, string timeUnitLabel)
    {
        if (string.Equals(timeUnitLabel, "samples", StringComparison.OrdinalIgnoreCase))
        {
            var rounded = Math.Round(value, 2);
            var isWhole = Math.Abs(rounded - Math.Round(rounded)) < 0.0001;
            var format = isWhole ? "N0" : "N2";
            return rounded.ToString(format, CultureInfo.InvariantCulture);
        }

        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private bool IsUnmanagedFrame(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Contains("UNMANAGED_CODE_TIME", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Unmanaged Code", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var ch in trimmed)
        {
            if (char.IsLetter(ch))
            {
                return false;
            }
        }

        return true;
    }

    private string FormatFunctionDisplayName(string rawName)
    {
        var formatted = NameFormatter.FormatMethodDisplayName(rawName);
        return GetCallTreeDisplayName(formatted);
    }

    private string FormatCallTreeName(string displayName, string matchName, bool isLeaf, string? nameColorOverride = null)
    {
        var escaped = Markup.Escape(displayName);
        if (isLeaf && ShouldStopAtLeaf(matchName))
        {
            return $"[{_theme.LeafHighlightColor}]{escaped}[/]";
        }

        var color = string.IsNullOrWhiteSpace(nameColorOverride) ? _theme.TextColor : nameColorOverride;
        return $"[{color}]{escaped}[/]";
    }

    private string GetCallTreeMatchName(CallTreeNode node)
    {
        return NameFormatter.FormatMethodDisplayName(node.Name);
    }

    private string GetCallTreeDisplayName(string matchName)
    {
        if (IsUnmanagedFrame(matchName))
        {
            return "Unmanaged Code";
        }

        return matchName;
    }

    private bool ShouldStopAtLeaf(string matchName)
    {
        return IsUnmanagedFrame(matchName) ||
               matchName.Contains("CastHelpers.", StringComparison.Ordinal) ||
               matchName.Contains("Array.Copy", StringComparison.Ordinal) ||
               matchName.Contains("Dictionary<__Canon,__Canon>.Resize", StringComparison.Ordinal) ||
               matchName.Contains("Buffer.BulkMoveWithWriteBarrier", StringComparison.Ordinal) ||
               matchName.Contains("SpanHelpers.SequenceEqual", StringComparison.Ordinal) ||
               matchName.Contains("HashSet<", StringComparison.Ordinal) ||
               matchName.Contains("Enumerable+ArrayWhereSelectIterator<", StringComparison.Ordinal) ||
               matchName.Contains("ImmutableDictionary<", StringComparison.Ordinal) ||
               matchName.Contains("SegmentedArrayBuilder<__Canon>.ToArray", StringComparison.Ordinal) ||
               matchName.Contains("__Canon", StringComparison.Ordinal) ||
               (matchName.Contains("List<", StringComparison.Ordinal) &&
                matchName.EndsWith(".ToArray", StringComparison.Ordinal));
    }

    private bool MatchesFunctionFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               FormatFunctionDisplayName(name).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<ExceptionTypeSample> FilterExceptionTypes(
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

    private string? SelectExceptionType(IReadOnlyList<ExceptionTypeSample> types, string? filter)
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

    private bool IsRuntimeNoise(string name)
    {
        var trimmed = name.TrimStart();
        var formatted = FormatFunctionDisplayName(trimmed);
        return IsUnmanagedFrame(trimmed) ||
               trimmed.Contains("(Non-Activities)", StringComparison.Ordinal) ||
               trimmed.Contains("Thread", StringComparison.Ordinal) ||
               trimmed.Contains("Threads", StringComparison.Ordinal) ||
               trimmed.Contains("Process", StringComparison.Ordinal) ||
               StartsWithDigits(trimmed) ||
               StartsWithDigits(formatted);
    }

    private bool StartsWithDigits(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.TrimStart();
        return trimmed.Length > 0 && char.IsDigit(trimmed[0]);
    }

    private string WrapAnsi(string text, string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return text;
        }

        return $"[{color}]{Markup.Escape(text)}[/]";
    }

    public string HighlightJitNumbers(string text)
    {
        return _jitNumberRegex.Replace(text, match => WrapAnsi(match.Value, _theme.AccentColor));
    }
}

public sealed record TableColumnSpec(string Header, bool RightAligned = false);
