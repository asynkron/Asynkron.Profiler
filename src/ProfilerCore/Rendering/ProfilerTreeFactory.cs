using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerTreeFactory
{
    private readonly Style _treeGuideStyle;

    public ProfilerTreeFactory(Theme theme)
    {
        _treeGuideStyle = new Style(ConsoleThemeHelpers.ParseHexColor(theme.TreeGuideColor));
    }

    public Tree Create(string rootLabel)
    {
        return new Tree(rootLabel)
        {
            Style = _treeGuideStyle,
            Guide = new CompactTreeGuide()
        };
    }
}
