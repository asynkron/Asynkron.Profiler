using System;
using System.IO;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal sealed class TraceLogSession : IDisposable
{
    private readonly TraceLog _traceLog;

    private TraceLogSession(TraceLog traceLog)
    {
        _traceLog = traceLog;
        Source = traceLog.Events.GetSource();
    }

    public TraceEventDispatcher Source { get; }

    public static TraceLogSession Open(string etlxPath)
    {
        var traceLog = TraceLog.OpenOrConvert(etlxPath, new TraceLogOptions { ConversionLog = TextWriter.Null });
        return new TraceLogSession(traceLog);
    }

    public void Dispose()
    {
        Source.Dispose();
        _traceLog.Dispose();
    }
}
