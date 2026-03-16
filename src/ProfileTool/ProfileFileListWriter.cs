using Spectre.Console;

namespace Asynkron.Profiler;

internal static class ProfileFileListWriter
{
    public static void Write(string title, string accentColor, IEnumerable<string> files)
    {
        AnsiConsole.MarkupLine($"[{accentColor}]{title}:[/]");
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }
    }
}
