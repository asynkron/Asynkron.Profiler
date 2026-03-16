using System;
using System.IO;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class TraceLogFileResolver
{
    public static string Resolve(string traceFile, string outputDirectory)
    {
        if (!traceFile.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            return traceFile;
        }

        var fileName = Path.GetFileNameWithoutExtension(traceFile);
        var targetPath = Path.Combine(outputDirectory, $"{fileName}.etlx");
        var options = new TraceLogOptions { ConversionLog = TextWriter.Null };
        return TraceLog.CreateFromEventPipeDataFile(traceFile, targetPath, options);
    }
}
