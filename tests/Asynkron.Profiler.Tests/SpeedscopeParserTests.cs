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
        var root = result.CallTreeRoot;
        var nodeA = root.Children.Values.Single(node => node.Name == "A");
        var nodeB = nodeA.Children.Values.Single(node => node.Name == "B");
        var nodeC = nodeA.Children.Values.Single(node => node.Name == "C");

        Assert.Equal(11d, nodeA.Total, 3);
        Assert.Equal(2, nodeA.Calls);
        Assert.Equal(2d, nodeB.Total, 3);
        Assert.Equal(2d, nodeC.Total, 3);

        var sampleA = result.AllFunctions.Single(sample => sample.Name == "A");
        var sampleB = result.AllFunctions.Single(sample => sample.Name == "B");
        var sampleC = result.AllFunctions.Single(sample => sample.Name == "C");

        AssertFunction(sampleA, 11d, 2);
        AssertFunction(sampleB, 2d, 1);
        AssertFunction(sampleC, 2d, 1);
    }

    [Fact]
    public void ParsesSampledProfilesWithWeights()
    {
        var result = Parse(CreateSpeedscopeJson(
            """
                {
                  "type": "sampled",
                  "unit": "samples",
                  "samples": [
                    [0, 1],
                    [0, 2]
                  ],
                  "weights": [2, 1]
                }
            """));

        Assert.Equal("samples", result.TimeUnitLabel);
        Assert.Equal("Samples", result.CountLabel);
        Assert.Equal(" samp", result.CountSuffix);
        var root = result.CallTreeRoot;
        var nodeA = root.Children.Values.Single(node => node.Name == "A");
        var nodeB = nodeA.Children.Values.Single(node => node.Name == "B");
        var nodeC = nodeA.Children.Values.Single(node => node.Name == "C");

        Assert.Equal(3d, nodeA.Total, 3);
        Assert.Equal(3, nodeA.Calls);
        Assert.Equal(2d, nodeB.Total, 3);
        Assert.Equal(2, nodeB.Calls);
        Assert.Equal(1d, nodeC.Total, 3);
        Assert.Equal(1, nodeC.Calls);

        var sampleA = result.AllFunctions.Single(sample => sample.Name == "A");
        var sampleB = result.AllFunctions.Single(sample => sample.Name == "B");
        var sampleC = result.AllFunctions.Single(sample => sample.Name == "C");

        AssertFunction(sampleA, 3d, 3);
        AssertFunction(sampleB, 2d, 2);
        AssertFunction(sampleC, 1d, 1);
    }

    private static CpuProfileResult Parse(string json)
    {
        var result = SpeedscopeParser.ParseJson(json);
        Assert.NotNull(result);
        return result!;
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

    private static void AssertFunction(FunctionSample sample, double expectedTime, int expectedCalls)
    {
        Assert.Equal(expectedTime, sample.TimeMs, 3);
        Assert.Equal(expectedCalls, sample.Calls);
    }
}
