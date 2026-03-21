using System.Globalization;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Asynkron.Profiler;

internal static class DotnetTraceProviderFactory
{
    private const string SampleProfilerProvider = "Microsoft-DotNETCore-SampleProfiler";

    public static string BuildCpuProviderList(bool includeException)
    {
        return includeException
            ? string.Join(",", [SampleProfilerProvider, BuildExceptionProvider()])
            : SampleProfilerProvider;
    }

    public static string BuildExceptionProvider()
    {
        var keywordsValue = ClrTraceEventParser.Keywords.Exception;
        var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
        return $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
    }

    public static string BuildContentionProvider()
    {
        var keywordsValue = ClrTraceEventParser.Keywords.Contention | ClrTraceEventParser.Keywords.Threading;
        var keywords = ((ulong)keywordsValue).ToString("x", CultureInfo.InvariantCulture);
        return $"Microsoft-Windows-DotNETRuntime:0x{keywords}:4";
    }
}
