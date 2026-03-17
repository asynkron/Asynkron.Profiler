using Spectre.Console;

namespace Asynkron.Profiler;

internal static class ProfilerRenderOutput
{
    public static void WriteNoResults(Theme theme)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No results to display[/]");
    }

    public static void WriteSection(string title, ProfileRenderRequest request)
    {
        ConsoleThemeHelpers.PrintSection($"{title}: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }
    }

    public static void WriteFilteredOutRuntimeMessage(int filteredOut)
    {
        if (filteredOut <= 0)
        {
            return;
        }

        var filteredOutText = filteredOut.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        AnsiConsole.MarkupLine($"[dim]Filtered out {filteredOutText} runtime frames. Use --include-runtime to show all.[/]");
    }

    public static void WriteFunctionFilterMessage(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        AnsiConsole.MarkupLine($"[dim]Filter: {Markup.Escape(filter)} (use --filter to change).[/]");
    }
}
