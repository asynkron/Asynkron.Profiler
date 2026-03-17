using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class MemoryProfileConsoleRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerConsoleTableWriter _tableWriter;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public MemoryProfileConsoleRenderer(
        Theme theme,
        ProfilerConsoleTableWriter tableWriter,
        ProfilerCallTreeRenderer callTreeRenderer)
    {
        _theme = theme;
        _tableWriter = tableWriter;
        _callTreeRenderer = callTreeRenderer;
    }

    public void Print(MemoryProfileResult? results, ProfileRenderRequest request)
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

        var rows = BuildSummaryRows(results);
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

        if (results.AllocationCallTree == null)
        {
            return;
        }

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

    private static List<IReadOnlyList<string>> BuildSummaryRows(MemoryProfileResult results)
    {
        var rows = new List<IReadOnlyList<string>>();
        AddRow(rows, "Iterations", results.Iterations);
        AddRow(rows, "Total time", results.TotalTime);
        AddRow(rows, "Per iteration (time)", results.PerIterationTime);
        AddRow(rows, "Total allocated", results.TotalAllocated);
        AddRow(rows, "Per iteration (allocated)", results.PerIterationAllocated);
        AddRow(rows, "GC Gen0 collections", results.Gen0Collections);
        AddRow(rows, "GC Gen1 collections", results.Gen1Collections);
        AddRow(rows, "GC Gen2 collections", results.Gen2Collections);
        AddRow(rows, "Parse (allocated)", results.ParseAllocated);
        AddRow(rows, "Evaluate (allocated)", results.EvaluateAllocated);
        AddRow(rows, "Heap before", results.HeapBefore);
        AddRow(rows, "Heap after", results.HeapAfter);
        return rows;
    }

    private static void AddRow(List<IReadOnlyList<string>> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new[] { label, Markup.Escape(value) });
        }
    }
}
