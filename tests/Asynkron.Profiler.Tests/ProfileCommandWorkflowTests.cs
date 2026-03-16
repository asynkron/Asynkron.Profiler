using System.Globalization;
using System.IO;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfileCommandWorkflowTests
{
    [Fact]
    public void TryParseHotThresholdAcceptsInvariantInput()
    {
        var success = ProfileTraceWorkflow.TryParseHotThreshold("0.25", out var value);

        Assert.True(success);
        Assert.Equal(0.25d, value, 3);
    }

    [Fact]
    public void CommandProcessStartInfoFactoryAddsArgumentsAndWorkingDirectory()
    {
        var command = new[] { "dotnet", "test", "--verbosity", "minimal" };
        var workingDirectory = Path.Combine(Path.GetTempPath(), "profile-tests");

        var startInfo = CommandProcessStartInfoFactory.Create(command, workingDirectory);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(workingDirectory, startInfo.WorkingDirectory);
        Assert.Equal(new[] { "test", "--verbosity", "minimal" }, startInfo.ArgumentList);
    }

    [Fact]
    public void TryParseHotThresholdRejectsOutOfRangeValue()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");

            var success = ProfileTraceWorkflow.TryParseHotThreshold("1,5", out _);

            Assert.False(success);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
