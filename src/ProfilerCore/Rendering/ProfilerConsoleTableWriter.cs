using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asynkron.Profiler;

internal sealed class ProfilerConsoleTableWriter
{
    internal readonly record struct TableRenderOptions(
        string? Title = null,
        bool HideHeaders = false,
        string? HeaderColor = null);

    private static readonly TableColumnSpec[] SummaryColumns =
    {
        new(string.Empty),
        new(string.Empty)
    };

    private readonly Theme _theme;

    public ProfilerConsoleTableWriter(Theme theme)
    {
        _theme = theme;
    }

    public static Table BuildTableWithRows(
        IReadOnlyList<TableColumnSpec> columns,
        IEnumerable<IReadOnlyList<string>> rows,
        TableRenderOptions? options = null)
    {
        var table = BuildTable(columns, options ?? default);
        foreach (var row in rows)
        {
            table.AddRow(row.ToArray());
        }

        return table;
    }

    public static void WriteTable(IReadOnlyList<TableColumnSpec> columns, IEnumerable<IReadOnlyList<string>> rows, TableRenderOptions? options = null) => AnsiConsole.Write(BuildTableWithRows(columns, rows, options));

    public static void WriteSummaryTable(IEnumerable<IReadOnlyList<string>> rows)
    {
        ConsoleThemeHelpers.PrintSection("Summary");
        WriteTable(SummaryColumns, rows, new TableRenderOptions(HideHeaders: true));
    }

    public static Rows BuildTableBlock(Table table, string title, string color)
    {
        return new Rows(
            new Markup($"[{color}]{Markup.Escape(title)}[/]"),
            table);
    }

    public Table? BuildAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
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

    public void PrintAllocationTable(IReadOnlyList<AllocationEntry> entries, string? allocationTotal)
    {
        var table = BuildAllocationTable(entries, allocationTotal);
        if (table == null)
        {
            return;
        }

        AnsiConsole.Write(table);
    }

    private static Table BuildTable(IReadOnlyList<TableColumnSpec> columns, TableRenderOptions options)
    {
        var table = new Table
        {
            Expand = false,
            ShowHeaders = !options.HideHeaders,
            ShowRowSeparators = false,
            Title = options.Title != null ? new TableTitle(options.Title) : null
        };

        table.Border(TableBorder.Rounded);
        table.BorderStyle(new Style(Color.Grey));

        foreach (var column in columns)
        {
            var columnHeader = column.Header;
            if (!string.IsNullOrWhiteSpace(options.HeaderColor))
            {
                columnHeader = $"[{options.HeaderColor}]{Markup.Escape(columnHeader)}[/]";
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
}
