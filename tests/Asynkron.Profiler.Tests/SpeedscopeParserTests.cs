using System.Linq;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class SpeedscopeParserTests
{
    [Fact]
    public void AggregatesAllProfilesInSpeedscope()
    {
        var result = Parse(CreateSpeedscopeJson(
            """
                {
                  "type": "evented",
                  "name": "Thread 1",
                  "events": [
                    { "type": "O", "frame": 0, "at": 0 },
                    { "type": "O", "frame": 1, "at": 1 },
                    { "type": "C", "frame": 1, "at": 3 },
                    { "type": "C", "frame": 0, "at": 5 }
                  ]
                },
                {
                  "type": "evented",
                  "name": "Thread 2",
                  "events": [
                    { "type": "O", "frame": 0, "at": 0 },
                    { "type": "O", "frame": 2, "at": 2 },
                    { "type": "C", "frame": 2, "at": 4 },
                    { "type": "C", "frame": 0, "at": 6 }
                  ]
                }
            """));

        Assert.Equal("ms", result.TimeUnitLabel);
        Assert.Equal("Calls", result.CountLabel);
        Assert.Equal("x", result.CountSuffix);
        var nodeA = AssertNode(result.CallTreeRoot, "A", 11d, 2);
        AssertNode(nodeA, "B", 2d, 1);
        AssertNode(nodeA, "C", 2d, 1);
        AssertFunctions(result, ("A", 11d, 2), ("B", 2d, 1), ("C", 2d, 1));
    }

    [Fact]
    public void ParsesSampledProfilesWithWeights()
    {
        var result = ParseSampled("samples", "[0, 1],\n                    [0, 2]", "2, 1");

        Assert.Equal("samples", result.TimeUnitLabel);
        Assert.Equal("Samples", result.CountLabel);
        Assert.Equal(" samp", result.CountSuffix);
        var nodeA = AssertNode(result.CallTreeRoot, "A", 3d, 3);
        AssertNode(nodeA, "B", 2d, 2);
        AssertNode(nodeA, "C", 1d, 1);
        AssertFunctions(result, ("A", 3d, 3), ("B", 2d, 2), ("C", 1d, 1));
    }

    [Fact]
    public void ConvertsTimeBasedSampleWeightsToMilliseconds()
    {
        var result = ParseSampled("microseconds", "[0]", "500");

        Assert.Equal("ms", result.TimeUnitLabel);
        Assert.Equal("Samples", result.CountLabel);
        Assert.Equal(" samp", result.CountSuffix);

        AssertFunctions(result, ("A", 0.5d, 1));
    }

    private static CpuProfileResult Parse(string json)
    {
        var result = SpeedscopeParser.ParseJson(json);
        Assert.NotNull(result);
        return result!;
    }

    private static CpuProfileResult ParseSampled(string unit, string samplesJson, string weightsJson)
    {
        return Parse(CreateSpeedscopeJson($$"""
            {
              "type": "sampled",
              "unit": "{{unit}}",
              "samples": [
                {{samplesJson}}
              ],
              "weights": [{{weightsJson}}]
            }
            """));
    }

    private static string CreateSpeedscopeJson(string profilesJson)
    {
        return $$"""
            {
              "shared": {
                "frames": [
                  { "name": "A" },
                  { "name": "B" },
                  { "name": "C" }
                ]
              },
              "profiles": [
            {{profilesJson}}
              ]
            }
            """;
    }

    private static CallTreeNode AssertNode(CallTreeNode parent, string name, double expectedTotal, int expectedCalls)
    {
        var node = parent.Children.Values.Single(child => child.Name == name);
        Assert.Equal(expectedTotal, node.Total, 3);
        Assert.Equal(expectedCalls, node.Calls);
        return node;
    }

    private static void AssertFunctions(CpuProfileResult result, params (string Name, double Time, int Calls)[] expectedFunctions)
    {
        foreach (var (name, time, calls) in expectedFunctions)
        {
            var sample = result.AllFunctions.Single(entry => entry.Name == name);
            AssertFunction(sample, time, calls);
        }
    }

    private static void AssertFunction(FunctionSample sample, double expectedTime, int expectedCalls)
    {
        Assert.Equal(expectedTime, sample.TimeMs, 3);
        Assert.Equal(expectedCalls, sample.Calls);
    }
}
