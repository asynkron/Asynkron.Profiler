using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Asynkron.Profiler;

internal static class TraceCallStackFrameEnumerator
{
    public static IEnumerable<string> EnumerateExceptionFrames(TraceCallStack? stack)
    {
        return EnumerateResolvedFrameNames(stack);
    }

    public static IEnumerable<string> EnumerateCpuFrames(TraceCallStack? stack)
    {
        var lastWasUnknown = false;
        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = GetFrameMethodName(current);
            if (methodName == null)
            {
                if (!lastWasUnknown)
                {
                    yield return "Unmanaged Code";
                    lastWasUnknown = true;
                }

                continue;
            }

            lastWasUnknown = false;
            yield return methodName;
        }
    }

    public static IEnumerable<string> EnumerateAllocationFrames(TraceCallStack stack)
    {
        for (var current = stack; current != null; current = current.Caller)
        {
            yield return GetFrameMethodName(current) ?? "Unknown";
        }
    }

    public static IEnumerable<string> EnumerateContentionFrames(TraceCallStack? stack)
    {
        return EnumerateResolvedFrameNames(stack);
    }

    public static string? GetTopFrameName(TraceCallStack? stack)
    {
        return GetFrameMethodName(stack);
    }

    private static string? GetFrameMethodName(TraceCallStack? stack)
    {
        var methodName = stack?.CodeAddress?.FullMethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = stack?.CodeAddress?.Method?.FullMethodName;
        }

        return string.IsNullOrWhiteSpace(methodName) ? null : methodName;
    }

    private static IEnumerable<string> EnumerateResolvedFrameNames(TraceCallStack? stack)
    {
        for (var current = stack; current != null; current = current.Caller)
        {
            var methodName = GetFrameMethodName(current);
            if (methodName != null)
            {
                yield return methodName;
            }
        }
    }
}
