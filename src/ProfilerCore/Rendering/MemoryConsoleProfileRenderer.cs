using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class MemoryConsoleProfileRenderer
{
    private readonly Theme _theme;
    private readonly ProfilerConsoleTableWriter _tableWriter;
    private readonly ProfilerCallTreeRenderer _callTreeRenderer;

    public MemoryConsoleProfileRenderer(
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
            ProfilerConsoleRenderHelpers.WriteMissingResults(_theme);
            return;
        }

        ProfilerConsoleRenderHelpers.WriteProfileHeader("MEMORY PROFILE", request);

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
}
