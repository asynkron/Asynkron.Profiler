using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal static class ProfilerConsoleRenderHelpers
{
    public static void WriteMissingResults(Theme theme)
    {
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No results to display[/]");
    }

    public static void WriteProfileHeader(string title, ProfileRenderRequest request)
    {
        ConsoleThemeHelpers.PrintSection($"{title}: {request.ProfileName}");
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            AnsiConsole.MarkupLine($"[dim]{request.Description}[/]");
        }
    }

    public static string? ResolveCallTreeRootFilter(string? rootFilter)
    {
        return string.IsNullOrWhiteSpace(rootFilter) ? null : rootFilter;
    }

    public static ProfilerTreeRootSelectionOptions CreateTreeRootSelectionOptions(
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
}
