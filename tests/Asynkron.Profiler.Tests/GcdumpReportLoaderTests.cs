using System.Collections.Generic;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class GcdumpReportLoaderTests
{
    [Fact]
    public void UsesParsedReportWhenToolSucceeds()
    {
        var messages = new List<string>();

        static HeapProfileResult Parse(string output) => new(output, [new HeapTypeEntry(16, 1, "Foo")]);

        var result = GcdumpReportLoader.Load(
            "input.gcdump",
            Theme.Current,
            (_, _, _, _) => (true, "parsed report", string.Empty),
            Parse,
            messages.Add);

        Assert.Equal("parsed report", result.RawOutput);
        Assert.Single(result.Types);
        Assert.Empty(messages);
    }

    [Fact]
    public void ReturnsRawReportAndLogsWhenToolFails()
    {
        var messages = new List<string>();

        var result = GcdumpReportLoader.Load(
            "input.gcdump",
            Theme.Current,
            (_, _, _, _) => (false, "raw output", "bad report"),
            _ => throw new Xunit.Sdk.XunitException("Parser should not be called"),
            messages.Add);

        Assert.Equal("raw output", result.RawOutput);
        Assert.Empty(result.Types);
        Assert.Single(messages);
        Assert.Contains("Could not parse gcdump", messages[0]);
    }
}
