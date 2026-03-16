using System;
using System.Globalization;
using Microsoft.Diagnostics.Tracing;

namespace Asynkron.Profiler;

internal static class TraceEventPayloadReader
{
    public static string GetExceptionTypeName(TraceEvent data)
    {
        var typeName = TryGetPayloadString(data, "ExceptionTypeName", "ExceptionType", "TypeName");
        if (string.IsNullOrWhiteSpace(typeName))
        {
            try
            {
                foreach (var payloadName in data.PayloadNames ?? Array.Empty<string>())
                {
                    if (!payloadName.Contains("ExceptionType", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = data.PayloadByName(payloadName);
                    if (value != null)
                    {
                        typeName = value.ToString();
                        break;
                    }
                }
            }
            catch
            {
                typeName = null;
            }
        }

        return string.IsNullOrWhiteSpace(typeName) ? "Unknown" : typeName;
    }

    public static double TryGetPayloadDurationMs(TraceEvent data)
    {
        var durationNs = TryGetPayloadLong(data, "DurationNs")
                         ?? TryGetPayloadLong(data, "DurationNS")
                         ?? TryGetPayloadLong(data, "Duration");
        if (durationNs is > 0)
        {
            return durationNs.Value / 1_000_000d;
        }

        return 0d;
    }

    private static string? TryGetPayloadString(TraceEvent data, params string[] names)
    {
        foreach (var name in names)
        {
            var value = TryGetPayload(data, name);
            if (value != null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static long? TryGetPayloadLong(TraceEvent data, string name)
    {
        var value = TryGetPayload(data, name);
        if (value == null)
        {
            return null;
        }

        return value switch
        {
            byte v => v,
            sbyte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v <= long.MaxValue ? (long)v : null,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }

    private static object? TryGetPayload(TraceEvent data, string name)
    {
        try
        {
            return data.PayloadByName(name);
        }
        catch
        {
            return null;
        }
    }
}
