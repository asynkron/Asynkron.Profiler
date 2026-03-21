using System.Text.RegularExpressions;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed partial class ProfilerJitNumberHighlighter
{
    private readonly Theme _theme;

    public ProfilerJitNumberHighlighter(Theme theme)
    {
        _theme = theme;
    }

    public string Highlight(string text)
    {
        return JitNumberRegex().Replace(text, match => WrapMarkup(match.Value, _theme.AccentColor));
    }

    private static string WrapMarkup(string text, string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return text;
        }

        return $"[{color}]{Markup.Escape(text)}[/]";
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(#?0x[0-9A-Fa-f]+|#?\d+)(?![A-Za-z0-9_])", RegexOptions.Compiled)]
    private static partial Regex JitNumberRegex();
}
