using System;
using System.IO;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal sealed class TraceFileLocator
{
    private readonly string _outputDirectory;

    public TraceFileLocator(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public string ResolveEtlxPath(string traceFile)
    {
        if (!traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            return traceFile;
        }

        var fileName = Path.GetFileNameWithoutExtension(traceFile);
        var targetPath = Path.Combine(_outputDirectory, $"{fileName}.etlx");
        var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
        return TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
    }
}
