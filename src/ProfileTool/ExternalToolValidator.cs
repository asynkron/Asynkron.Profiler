using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Asynkron.Profiler;

internal enum ExternalToolStatus
{
    Available,
    Missing,
    InvalidVersion,
    VersionTooOld
}

internal readonly record struct ExternalToolRequirement(
    string ToolName,
    string InstallCommand,
    string UpdateCommand,
    Version MinimumVersion);

internal readonly record struct ExternalToolCheckResult(
    ExternalToolStatus Status,
    string Detail,
    Version? ActualVersion = null)
{
    public bool IsSatisfied => Status == ExternalToolStatus.Available;
}

internal static partial class ExternalToolValidator
{
    internal static readonly ExternalToolRequirement DotnetTrace =
        new(
            "dotnet-trace",
            "dotnet tool install -g dotnet-trace",
            "dotnet tool update -g dotnet-trace",
            new Version(9, 0, 661903));

    internal static readonly ExternalToolRequirement DotnetGcdump =
        new(
            "dotnet-gcdump",
            "dotnet tool install -g dotnet-gcdump",
            "dotnet tool update -g dotnet-gcdump",
            new Version(9, 0, 661903));

    internal static ExternalToolCheckResult Validate(
        ExternalToolRequirement requirement,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess)
    {
        var (success, stdout, stderr) = runProcess(requirement.ToolName, ["--version"], null, 10000);
        if (!success)
        {
            var detail = FirstNonEmptyLine(stderr)
                         ?? FirstNonEmptyLine(stdout)
                         ?? "Tool not found.";
            return new ExternalToolCheckResult(ExternalToolStatus.Missing, detail);
        }

        var versionText = string.Join(
            Environment.NewLine,
            new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));

        if (!TryParseVersion(versionText, out var actualVersion))
        {
            var detail = string.IsNullOrWhiteSpace(versionText)
                ? "Version output was empty."
                : versionText.Trim();
            return new ExternalToolCheckResult(ExternalToolStatus.InvalidVersion, detail);
        }

        if (actualVersion < requirement.MinimumVersion)
        {
            return new ExternalToolCheckResult(
                ExternalToolStatus.VersionTooOld,
                $"Found {actualVersion}",
                actualVersion);
        }

        return new ExternalToolCheckResult(
            ExternalToolStatus.Available,
            $"Found {actualVersion}",
            actualVersion);
    }

    internal static string BuildFailureMessage(
        ExternalToolRequirement requirement,
        ExternalToolCheckResult result)
    {
        return result.Status switch
        {
            ExternalToolStatus.Missing =>
                $"{requirement.ToolName} unavailable: {result.Detail}{Environment.NewLine}" +
                $"Required version: >= {requirement.MinimumVersion}{Environment.NewLine}" +
                $"Install: {requirement.InstallCommand}",
            ExternalToolStatus.InvalidVersion =>
                $"{requirement.ToolName} version check failed: {result.Detail}{Environment.NewLine}" +
                $"Required version: >= {requirement.MinimumVersion}{Environment.NewLine}" +
                $"Update: {requirement.UpdateCommand}",
            ExternalToolStatus.VersionTooOld =>
                $"{requirement.ToolName} is too old: {result.ActualVersion}{Environment.NewLine}" +
                $"Required version: >= {requirement.MinimumVersion}{Environment.NewLine}" +
                $"Update: {requirement.UpdateCommand}",
            _ => string.Empty
        };
    }

    internal static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionRegex().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var groups = match.Groups;
        if (!int.TryParse(groups[1].Value, out var major) ||
            !int.TryParse(groups[2].Value, out var minor) ||
            !int.TryParse(groups[3].Value, out var build))
        {
            return false;
        }

        if (groups[4].Success && int.TryParse(groups[4].Value, out var revision))
        {
            version = new Version(major, minor, build, revision);
            return true;
        }

        version = new Version(major, minor, build);
        return true;
    }

    private static string? FirstNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    [GeneratedRegex(@"(?<!\d)(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionRegex();
}
