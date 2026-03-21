using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfileInputLoaderTests
{
    [Fact]
    public void LoadHeap_UsesGcdumpLoaderWhenToolIsAvailable()
    {
        var messages = new List<string>();
        var toolChecks = new List<(string Tool, string Hint)>();
        var gcdumpPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gcdump");
        File.WriteAllText(gcdumpPath, "placeholder");

        try
        {
            var loader = CreateLoader(
                ensureToolAvailable: (tool, hint) =>
                {
                    toolChecks.Add((tool, hint));
                    return true;
                },
                runProcess: (_, _, _, _) => (true, "parsed report", string.Empty),
                parseGcdumpReport: output => new HeapProfileResult(output, [new HeapTypeEntry(16, 1, "Foo")]),
                messages: messages);

            var result = loader.LoadHeap(gcdumpPath);

            Assert.NotNull(result);
            Assert.Equal("parsed report", result!.RawOutput);
            Assert.Single(result.Types);
            Assert.Single(toolChecks);
            Assert.Equal("dotnet-gcdump", toolChecks[0].Tool);
            Assert.Empty(messages);
        }
        finally
        {
            File.Delete(gcdumpPath);
        }
    }

    [Fact]
    public void LoadHeap_RejectsUnsupportedInputs()
    {
        var messages = new List<string>();
        var unsupportedPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        File.WriteAllText(unsupportedPath, "placeholder");

        try
        {
            var loader = CreateLoader((_, _) => true, messages: messages);

            var result = loader.LoadHeap(unsupportedPath);

            Assert.Null(result);
            Assert.Single(messages);
            Assert.Contains("Unsupported heap input", messages[0], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(unsupportedPath);
        }
    }

    private static ProfileInputLoader CreateLoader(
        Func<string, string, bool> ensureToolAvailable,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)>? runProcess = null,
        Func<string, HeapProfileResult>? parseGcdumpReport = null,
        List<string>? messages = null)
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        return new ProfileInputLoader(
            new ProfilerTraceAnalyzer(outputDir),
            () => Theme.Current,
            ensureToolAvailable,
            runProcess ?? ((_, _, _, _) => (true, string.Empty, string.Empty)),
            parseGcdumpReport ?? (output => new HeapProfileResult(output, [])),
            message => messages?.Add(message),
            "dotnet tool install -g dotnet-gcdump");
    }
}
