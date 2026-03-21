using System.Linq;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class SpeedscopeParserTests
{
    [Fact]
    public void ReturnsNullForInvalidDocumentShape()
    {
        var result = SpeedscopeParser.ParseJson(
            """
            {
              "shared": {},
              "profiles": []
            }
            """);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNullWhenProfilesContainNoParsableSamples()
    {
        var result = SpeedscopeParser.ParseJson(
            """
            {
              "shared": {
                "frames": [
                  { "name": "A" }
                ]
              },
              "profiles": [
                {
                  "type": "evented",
                  "name": "Thread 1",
                  "events": "invalid"
                }
              ]
            }
            """);

        Assert.Null(result);
    }

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

    [Fact]
    public void CapturesSelfTimeAndTimingBoundsForEventedProfiles()
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
                }
            """));

        var root = result.CallTreeRoot;
        var nodeA = root.Children.Values.Single(node => node.Name == "A");
        var nodeB = nodeA.Children.Values.Single(node => node.Name == "B");

        Assert.Equal(3d, nodeA.Self, 3);
        Assert.Equal(2d, nodeB.Self, 3);
        Assert.True(root.HasTiming);
        Assert.Equal(0d, root.MinStart, 3);
        Assert.Equal(5d, root.MaxEnd, 3);
    }

    [Fact]
    public void ConvertsSecondUnitsToMilliseconds()
    {
        var result = Parse(CreateSpeedscopeJson(
            """
                {
                  "type": "sampled",
                  "unit": "seconds",
                  "samples": [
                    [0],
                    [0]
                  ],
                  "weights": [0.001, 0.002]
                }
            """));

        var sampleA = result.AllFunctions.Single(sample => sample.Name == "A");
        Assert.Equal("ms", result.TimeUnitLabel);
        Assert.Equal("Samples", result.CountLabel);
        Assert.Equal(" samp", result.CountSuffix);
        Assert.Equal(3d, sampleA.TimeMs, 3);
        Assert.Equal(2, sampleA.Calls);
    }

    [Fact]
    public void UsesUnknownForBlankMissingAndOutOfRangeFrames()
    {
        var result = Parse(
            """
            {
              "shared": {
                "frames": [
                  { "name": "" },
                  {}
                ]
              },
              "profiles": [
                {
                  "type": "sampled",
                  "samples": [
                    [0, 1, 2]
                  ]
                }
              ]
            }
            """);

        Assert.Equal(3, result.AllFunctions.Count);
        Assert.All(result.AllFunctions, sample => Assert.Equal("Unknown", sample.Name));
        var unknownLeaf = result.CallTreeRoot.Children.Values.Single().Children.Values.Single().Children.Values.Single();
        Assert.Equal("Unknown", unknownLeaf.Name);
    }

    [Fact]
    public void UsesSampleLabelsWhenMixingEventedAndSampledProfiles()
    {
        var result = Parse(CreateSpeedscopeJson(
            """
                {
                  "type": "evented",
                  "name": "Thread 1",
                  "events": [
                    { "type": "O", "frame": 0, "at": 0 },
                    { "type": "C", "frame": 0, "at": 1 }
                  ]
                },
                {
                  "type": "sampled",
                  "unit": "samples",
                  "samples": [
                    [0, 1]
                  ],
                  "weights": [2]
                }
            """));

        Assert.Equal("ms", result.TimeUnitLabel);
        Assert.Equal("Samples", result.CountLabel);
        Assert.Equal(" samp", result.CountSuffix);
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
