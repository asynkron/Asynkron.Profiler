using System;
using System.IO;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfileArtifactPathBuilderTests
{
    [Fact]
    public void Build_FormatsTimestampedArtifactPath()
    {
        var path = ProfileArtifactPathBuilder.Build(
            Path.Combine("tmp", "profiles"),
            "demo",
            "nettrace",
            new DateTime(2026, 3, 20, 21, 15, 42));

        Assert.Equal(Path.Combine("tmp", "profiles", "demo_20260320_211542.nettrace"), path);
    }
}
