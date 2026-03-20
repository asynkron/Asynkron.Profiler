using System.IO;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfilerThemeRuntimeTests
{
    [Fact]
    public void TryApply_UsesResolvedThemeAndRefreshesServices()
    {
        var originalTheme = Theme.Current;

        try
        {
            var runtime = new ProfilerThemeRuntime(Path.GetTempPath(), Theme.Default, _ => { });
            var originalRenderer = runtime.Renderer;

            var applied = runtime.TryApply("nord");

            Assert.True(applied);
            Assert.Equal(Theme.Nord.AccentColor, runtime.CurrentTheme.AccentColor);
            Assert.Equal(Theme.Nord.AccentColor, Theme.Current.AccentColor);
            Assert.NotSame(originalRenderer, runtime.Renderer);
        }
        finally
        {
            Theme.Current = originalTheme;
        }
    }

    [Fact]
    public void TryApply_RejectsUnknownThemeWithoutReplacingServices()
    {
        var originalTheme = Theme.Current;

        try
        {
            var runtime = new ProfilerThemeRuntime(Path.GetTempPath(), Theme.Monokai, _ => { });
            var originalRenderer = runtime.Renderer;

            var applied = runtime.TryApply("does-not-exist");

            Assert.False(applied);
            Assert.Equal(Theme.Monokai.AccentColor, runtime.CurrentTheme.AccentColor);
            Assert.Equal(Theme.Monokai.AccentColor, Theme.Current.AccentColor);
            Assert.Same(originalRenderer, runtime.Renderer);
        }
        finally
        {
            Theme.Current = originalTheme;
        }
    }
}
