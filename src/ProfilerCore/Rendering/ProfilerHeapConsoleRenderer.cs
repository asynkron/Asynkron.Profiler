using System.Collections.Generic;
using System.Globalization;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerHeapConsoleRenderer
{
    private readonly Theme _theme;

    public ProfilerHeapConsoleRenderer(Theme theme)
    {
        _theme = theme;
    }

    public void Print(HeapProfileResult? results, ProfileRenderRequest request)
    {
        if (results is null)
        {
            ProfilerRenderOutput.WriteNoResults(_theme);
            return;
        }

        ProfilerRenderOutput.WriteSection("HEAP SNAPSHOT", request);
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
}
