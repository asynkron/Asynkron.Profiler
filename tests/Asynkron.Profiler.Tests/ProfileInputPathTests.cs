using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfileInputPathTests
{
    [Fact]
    public void ApplyDefaults_MapsExtensionsToExpectedModes()
    {
        AssertModes("trace.json", expectedCpu: true, expectedMemory: false, expectedHeap: false, expectedException: false, expectedContention: false);
        AssertModes("trace.nettrace", expectedCpu: true, expectedMemory: false, expectedHeap: false, expectedException: true, expectedContention: true);
        AssertModes("trace.etlx", expectedCpu: false, expectedMemory: true, expectedHeap: false, expectedException: true, expectedContention: true);
        AssertModes("trace.gcdump", expectedCpu: false, expectedMemory: false, expectedHeap: true, expectedException: false, expectedContention: false);
        AssertModes("trace.unknown", expectedCpu: true, expectedMemory: false, expectedHeap: false, expectedException: false, expectedContention: false);
    }

    [Fact]
    public void BuildLabel_FallsBackToInputWhenFileNameIsMissing()
    {
        var label = ProfileInputPath.BuildLabel(string.Empty);

        Assert.Equal("input", label);
    }

    [Theory]
    [InlineData("trace.json", "Speedscope")]
    [InlineData("trace.nettrace", "NetTrace")]
    [InlineData("trace.etlx", "Etlx")]
    [InlineData("trace.gcdump", "Gcdump")]
    [InlineData("trace.txt", "HeapReport")]
    [InlineData("trace.log", "HeapReport")]
    [InlineData("trace.bin", "Unknown")]
    public void GetKind_ReturnsExpectedInputKind(string inputPath, string expectedKind)
    {
        Assert.Equal(expectedKind, ProfileInputPath.GetKind(inputPath).ToString());
    }

    private static void AssertModes(
        string inputPath,
        bool expectedCpu,
        bool expectedMemory,
        bool expectedHeap,
        bool expectedException,
        bool expectedContention)
    {
        var runCpu = false;
        var runMemory = false;
        var runHeap = false;
        var runException = false;
        var runContention = false;

        ProfileInputPath.ApplyDefaults(
            inputPath,
            ref runCpu,
            ref runMemory,
            ref runHeap,
            ref runException,
            ref runContention);

        Assert.Equal(expectedCpu, runCpu);
        Assert.Equal(expectedMemory, runMemory);
        Assert.Equal(expectedHeap, runHeap);
        Assert.Equal(expectedException, runException);
        Assert.Equal(expectedContention, runContention);
    }
}
