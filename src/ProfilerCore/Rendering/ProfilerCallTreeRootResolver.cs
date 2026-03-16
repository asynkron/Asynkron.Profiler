using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerCallTreeRootResolver
{
    private readonly Theme _theme;

    public ProfilerCallTreeRootResolver(Theme theme)
    {
        _theme = theme;
    }

    public ProfilerCallTreeRootSelection Resolve(
        CallTreeNode callTreeRoot,
        double totalTime,
        string title,
        string? rootFilter,
        bool includeRuntime,
        string? rootMode)
    {
        if (string.IsNullOrWhiteSpace(rootFilter))
        {
            return new ProfilerCallTreeRootSelection(callTreeRoot, totalTime, title);
        }

        var matches = FindCallTreeMatches(callTreeRoot, rootFilter);
        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No call tree nodes matched '{Markup.Escape(rootFilter)}'. Showing full tree.[/]");
            return new ProfilerCallTreeRootSelection(callTreeRoot, totalTime, title);
        }

        var rootNode = SelectRootMatch(matches, includeRuntime, rootMode);
        var rootTotal = GetCallTreeTime(rootNode, useSelfTime: false);
        return new ProfilerCallTreeRootSelection(rootNode, rootTotal, $"{title} - root: {Markup.Escape(rootFilter)}");
    }
}

internal sealed record ProfilerCallTreeRootSelection(
    CallTreeNode RootNode,
    double RootTotal,
    string Title);
