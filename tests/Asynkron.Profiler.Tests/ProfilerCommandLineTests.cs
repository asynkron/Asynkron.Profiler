using System;
using System.Globalization;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfilerCommandLineTests
{
    [Fact]
    public void ParsesExceptionAliasThemeAliasAndHotThreshold()
    {
        var commandLine = new ProfilerCommandLine();
        var parseResult = commandLine.BuildParser().Parse(["--exceptions", "-t", "nord", "--hot", "0.6", "--input", "trace.nettrace"]);

        var success = commandLine.TryCreateInvocation(parseResult, out var invocation, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.True(invocation.Exception);
        Assert.Equal("nord", invocation.ThemeName);
        Assert.True(invocation.HotThresholdSpecified);
        Assert.Equal(0.6d, invocation.HotThreshold, 3);
    }

    [Fact]
    public void AcceptsCurrentCultureHotThresholdFormat()
    {
        using var _ = new CultureScope("sv-SE");
        var commandLine = new ProfilerCommandLine();
        var parseResult = commandLine.BuildParser().Parse(["--hot", "0,5", "--input", "trace.nettrace"]);

        var success = commandLine.TryCreateInvocation(parseResult, out var invocation, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        Assert.Equal(0.5d, invocation.HotThreshold, 3);
    }

    [Fact]
    public void RejectsOutOfRangeHotThreshold()
    {
        var commandLine = new ProfilerCommandLine();
        var parseResult = commandLine.BuildParser().Parse(["--hot", "1.1", "--input", "trace.nettrace"]);

        var success = commandLine.TryCreateInvocation(parseResult, out _, out var errorMessage);

        Assert.False(success);
        Assert.Equal("--hot must be a number between 0 and 1 (use 0.3 or 0,3).", errorMessage);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUiCulture;

        public CultureScope(string cultureName)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUiCulture = CultureInfo.CurrentUICulture;

            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
