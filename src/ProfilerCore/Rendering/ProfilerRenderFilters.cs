using System;
using System.Collections.Generic;
using System.Linq;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

public static class ProfilerRenderFilters
{
    public static string? NormalizeCallTreeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }

    internal static ProfilerTreeRootSelectionOptions BuildTreeRootSelectionOptions(
        ProfileRenderRequest request,
        string? rootFilter)
    {
        return new ProfilerTreeRootSelectionOptions(
            rootFilter,
            request.IncludeRuntime,
            request.CallTreeDepth,
            request.CallTreeWidth,
            request.CallTreeRootMode,
            request.CallTreeSiblingCutoffPercent);
    }

    public static bool MatchesFunctionFilter(string name, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               FormatFunctionDisplayName(name).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ExceptionTypeSample> FilterExceptionTypes(
        IReadOnlyList<ExceptionTypeSample> types,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return types;
        }

        return types
            .Where(entry => entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            NameFormatter.FormatTypeDisplayName(entry.Type)
                                .Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string? SelectExceptionType(IReadOnlyList<ExceptionTypeSample> types, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        foreach (var entry in types)
        {
            if (entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                NameFormatter.FormatTypeDisplayName(entry.Type)
                    .Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Type;
            }
        }

        return null;
    }

    internal static bool IsVisibleAtRuntime(string name, bool includeRuntime)
    {
        return includeRuntime || !IsRuntimeNoise(name);
    }
}
