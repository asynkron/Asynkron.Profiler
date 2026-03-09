using System;
using System.Collections.Generic;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ExternalToolValidatorTests
{
    [Theory]
    [InlineData("9.0.661903", "9.0.661903")]
    [InlineData("9.0.652701+240cb1ce5bc30594c515206764241d7982e384af", "9.0.652701")]
    [InlineData("dotnet-trace version 10.0.100-preview.1", "10.0.100")]
    public void ParsesSemanticVersionPrefix(string input, string expected)
    {
        var success = ExternalToolValidator.TryParseVersion(input, out var actual);

        Assert.True(success);
        Assert.Equal(Version.Parse(expected), actual);
    }

    [Fact]
    public void RejectsMissingVersionText()
    {
        var success = ExternalToolValidator.TryParseVersion("version unknown", out _);

        Assert.False(success);
    }

    [Fact]
    public void ReportsMissingToolWithInstallCommand()
    {
        var result = ExternalToolValidator.Validate(
            ExternalToolValidator.DotnetTrace,
            static (fileName, args, workingDir, timeoutMs) => (false, string.Empty, "No such file or directory"));

        Assert.Equal(ExternalToolStatus.Missing, result.Status);

        var message = ExternalToolValidator.BuildFailureMessage(ExternalToolValidator.DotnetTrace, result);
        Assert.Contains("dotnet-trace unavailable", message);
        Assert.Contains("Required version: >= 9.0.661903", message);
        Assert.Contains("dotnet tool install -g dotnet-trace", message);
    }

    [Fact]
    public void ReportsOldToolVersionWithUpdateCommand()
    {
        var result = ExternalToolValidator.Validate(
            ExternalToolValidator.DotnetTrace,
            static (fileName, args, workingDir, timeoutMs) => (true, "9.0.652701+abc", string.Empty));

        Assert.Equal(ExternalToolStatus.VersionTooOld, result.Status);
        Assert.Equal(new Version(9, 0, 652701), result.ActualVersion);

        var message = ExternalToolValidator.BuildFailureMessage(ExternalToolValidator.DotnetTrace, result);
        Assert.Contains("dotnet-trace is too old: 9.0.652701", message);
        Assert.Contains("Required version: >= 9.0.661903", message);
        Assert.Contains("dotnet tool update -g dotnet-trace", message);
    }

    [Fact]
    public void AcceptsMinimumSupportedDotnetTraceVersion()
    {
        var result = ExternalToolValidator.Validate(
            ExternalToolValidator.DotnetTrace,
            static (fileName, args, workingDir, timeoutMs) => (true, "9.0.661903", string.Empty));

        Assert.True(result.IsSatisfied);
        Assert.Equal(new Version(9, 0, 661903), result.ActualVersion);
    }

    [Fact]
    public void AcceptsMinimumSupportedDotnetGcdumpVersion()
    {
        var result = ExternalToolValidator.Validate(
            ExternalToolValidator.DotnetGcdump,
            static (fileName, args, workingDir, timeoutMs) => (true, "9.0.661903", string.Empty));

        Assert.True(result.IsSatisfied);
        Assert.Equal(new Version(9, 0, 661903), result.ActualVersion);
    }

    [Fact]
    public void ReportsInvalidVersionOutput()
    {
        var result = ExternalToolValidator.Validate(
            ExternalToolValidator.DotnetGcdump,
            static (fileName, args, workingDir, timeoutMs) => (true, "version unknown", string.Empty));

        Assert.Equal(ExternalToolStatus.InvalidVersion, result.Status);

        var message = ExternalToolValidator.BuildFailureMessage(ExternalToolValidator.DotnetGcdump, result);
        Assert.Contains("dotnet-gcdump version check failed", message);
        Assert.Contains("Required version: >= 9.0.661903", message);
        Assert.Contains("dotnet tool update -g dotnet-gcdump", message);
    }
}
