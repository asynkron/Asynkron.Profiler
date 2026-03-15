using System;
using Spectre.Console;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ConsoleThemeHelpersTests
{
    [Theory]
    [InlineData("#A1B2C3", 0xA1, 0xB2, 0xC3)]
    [InlineData(" 445566 ", 0x44, 0x55, 0x66)]
    public void TryParseHexColorAcceptsSixDigitHex(string value, byte r, byte g, byte b)
    {
        var success = ConsoleThemeHelpers.TryParseHexColor(value, out var rgb);

        Assert.True(success);
        Assert.Equal((r, g, b), rgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("#12345G")]
    public void TryParseHexColorRejectsInvalidValues(string? value)
    {
        var success = ConsoleThemeHelpers.TryParseHexColor(value, out var rgb);

        Assert.False(success);
        Assert.Equal(default, rgb);
    }

    [Fact]
    public void ParseHexColorReturnsSpectreColor()
    {
        var color = ConsoleThemeHelpers.ParseHexColor("#7D7D7D");

        Assert.Equal(new Color(0x7D, 0x7D, 0x7D), color);
    }

    [Fact]
    public void ParseHexColorReturnsDefaultForEmptyValues()
    {
        var color = ConsoleThemeHelpers.ParseHexColor(null);

        Assert.Equal(Color.Default, color);
    }

    [Fact]
    public void ParseHexColorThrowsForInvalidValues()
    {
        Assert.Throws<FormatException>(() => ConsoleThemeHelpers.ParseHexColor("plum1"));
    }
}
