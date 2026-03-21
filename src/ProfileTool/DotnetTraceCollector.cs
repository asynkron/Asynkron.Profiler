using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class DotnetTraceCollector
{
    private readonly ProfileCollectionServices _services;

    public DotnetTraceCollector(ProfileCollectionServices services)
    {
        _services = services;
    }

    public string? Collect(
        string[] command,
        string label,
        string traceSuffix,
        string failureLabel,
        Action<List<string>> configureCollectArgs)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(configureCollectArgs);

        if (!_services.EnsureToolAvailable("dotnet-trace", ProfileCollectionRunner.DotnetTraceInstallHint))
        {
            return null;
        }

        var traceFile = ProfileArtifactPathBuilder.Build(_services.OutputDirectory, label, traceSuffix);
        var collectArgs = new List<string> { "collect" };
        configureCollectArgs(collectArgs);
        collectArgs.Add("--output");
        collectArgs.Add(traceFile);
        collectArgs.Add("--");
        collectArgs.AddRange(command);

        var (success, _, stderr) = _services.RunProcess("dotnet-trace", collectArgs, null, 180000);
        if (success && File.Exists(traceFile))
        {
            return traceFile;
        }

        var detail = string.IsNullOrWhiteSpace(stderr) ? "Trace file was not created." : stderr;
        _services.WriteFailure(failureLabel, detail);
        return null;
    }
}
