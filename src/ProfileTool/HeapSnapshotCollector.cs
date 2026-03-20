using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
namespace Asynkron.Profiler;

internal sealed class HeapSnapshotCollector
{
    private readonly ProfileCollectionServices _services;

    public HeapSnapshotCollector(ProfileCollectionServices services)
    {
        _services = services;
    }

    public string? Collect(string[] command, string label)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_services.EnsureToolAvailable("dotnet-gcdump", ProfileCollectionRunner.DotnetGcdumpInstallHint))
        {
            return null;
        }

        if (command.Length == 0)
        {
            _services.WriteError("No command provided for heap snapshot.");
            return null;
        }

        var gcdumpFile = ProfileArtifactPathBuilder.Build(_services.OutputDirectory, label, "gcdump");

        try
        {
            using var process = Process.Start(CommandStartInfoFactory.Create(command));
            if (process == null)
            {
                _services.WriteError("Failed to start process for heap snapshot.");
                return null;
            }

            Thread.Sleep(500);

            var (success, _, stderr) = _services.RunProcess(
                "dotnet-gcdump",
                ["collect", "-p", process.Id.ToString(CultureInfo.InvariantCulture), "-o", gcdumpFile],
                null,
                60000);

            process.WaitForExit();

            if (success && File.Exists(gcdumpFile))
            {
                return gcdumpFile;
            }

            var detail = string.IsNullOrWhiteSpace(stderr) ? "GC dump file was not created." : stderr;
            _services.WriteFailure("GC dump collection failed", detail);
            return null;
        }
        catch (Exception ex)
        {
            _services.WriteFailure("GC dump collection failed", ex.Message);
            return null;
        }
    }
}
