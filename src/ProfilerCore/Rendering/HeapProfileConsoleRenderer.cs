using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class HeapProfileConsoleRenderer
{
    private readonly Theme _theme;

    public HeapProfileConsoleRenderer(Theme theme)
    {
        _theme = theme;
    }

    public void Print(HeapProfileResult? results, ProfileRenderRequest request)
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
            RenderTypes(results.Types);
            return;
        }

        if (!string.IsNullOrWhiteSpace(results.RawOutput))
        {
            AnsiConsole.WriteLine(results.RawOutput);
        }
    }

    private static void RenderTypes(IReadOnlyList<HeapTypeEntry> types)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var entry in types.Take(40))
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
}
