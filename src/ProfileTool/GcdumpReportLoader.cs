using System;
using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal static class GcdumpReportLoader
{
    public static HeapProfileResult Load(
        string gcdumpPath,
        Theme theme,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Func<string, HeapProfileResult> parseReport,
        Action<string> writeLine)
    {
        var (reportSuccess, reportOut, reportErr) = runProcess(
            "dotnet-gcdump",
            ["report", gcdumpPath],
            null,
            60000);

        if (reportSuccess)
        {
            return parseReport(reportOut);
        }

        writeLine($"[{theme.AccentColor}]Could not parse gcdump, showing raw output:[/] {Markup.Escape(reportErr)}");
        return new HeapProfileResult(reportOut, Array.Empty<HeapTypeEntry>());
    }
}
