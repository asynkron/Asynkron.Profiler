using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfilerJitNumberHighlighterTests
{
    [Fact]
    public void HighlightWrapsDecimalAndHexTokensWithAccentColor()
    {
        var renderer = new ProfilerConsoleRenderer(new Theme { AccentColor = "gold1" });

        var highlighted = renderer.HighlightJitNumbers("mov eax, 42 ; addr 0x1234");

        Assert.Equal("mov eax, [gold1]42[/] ; addr [gold1]0x1234[/]", highlighted);
    }

    [Fact]
    public void HighlightLeavesEmbeddedIdentifiersUntouched()
    {
        var renderer = new ProfilerConsoleRenderer(new Theme { AccentColor = "gold1" });

        var highlighted = renderer.HighlightJitNumbers("LBB0_4 keeps label0x10 intact");

        Assert.Equal("LBB0_4 keeps label0x10 intact", highlighted);
    }
}
