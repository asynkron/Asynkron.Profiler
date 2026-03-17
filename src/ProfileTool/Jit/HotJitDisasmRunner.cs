using System.Collections.Generic;
using System.Globalization;
using Spectre.Console;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class HotJitDisasmRunner
{
    private readonly Theme _theme;
    private readonly JitCommandRunner _jitCommandRunner;
    private readonly JitOutputFormatter _jitOutputFormatter;
    private readonly Action<string, IEnumerable<string>> _writeOutputFiles;

    public HotJitDisasmRunner(
        Theme theme,
        JitCommandRunner jitCommandRunner,
        JitOutputFormatter jitOutputFormatter,
        Action<string, IEnumerable<string>> writeOutputFiles)
    {
        _theme = theme;
        _jitCommandRunner = jitCommandRunner;
        _jitOutputFormatter = jitOutputFormatter;
        _writeOutputFiles = writeOutputFiles;
    }

    public void Run(ProfilerExecutionRequest request, CpuProfileResult results)
    {
        var rootNode = results.CallTreeRoot;
        var totalTime = results.CallTreeTotal;
        var totalSamples = rootNode.Calls;
        var title = $"JIT DISASM (HOT METHODS >= {request.RenderRequest.HotThreshold.ToString("F1", CultureInfo.InvariantCulture)})";

        if (!string.IsNullOrWhiteSpace(request.RenderRequest.CallTreeRoot))
        {
            var matches = FindCallTreeMatches(rootNode, request.RenderRequest.CallTreeRoot);
            if (matches.Count > 0)
            {
                rootNode = SelectRootMatch(matches, request.RenderRequest.IncludeRuntime, request.RenderRequest.CallTreeRootMode);
                totalTime = GetCallTreeTime(rootNode, useSelfTime: false);
                totalSamples = rootNode.Calls;
                title = $"{title} - root: {Markup.Escape(request.RenderRequest.CallTreeRoot)}";
            }
        }

        var hotMethods = CollectHotMethods(
            rootNode,
            totalTime,
            totalSamples,
            request.RenderRequest.IncludeRuntime,
            request.RenderRequest.HotThreshold);
        ConsoleThemeHelpers.PrintSection(title, _theme.AccentColor);
        if (hotMethods.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.AccentColor}]No hot methods found.[/]");
            return;
        }

        var index = 1;
        foreach (var method in hotMethods)
        {
            AnsiConsole.MarkupLine(
                $"[{_theme.AccentColor}]Disassembling ({index}/{hotMethods.Count}):[/] {Markup.Escape(method.DisplayName)}");

            var dumpFiles = _jitCommandRunner.RunDisasm(request.Command, method.Filter, suppressNoMarkersWarning: true);
            var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
            var hasMarkers = JitCommandRunner.HasDisasmMarkers(logPath ?? string.Empty);

            if (!hasMarkers)
            {
                AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No JIT disassembly markers found. Check the method filter.[/]");
            }

            _writeOutputFiles("JIT disasm files", dumpFiles);

            if (hasMarkers && !string.IsNullOrWhiteSpace(logPath))
            {
                _jitOutputFormatter.PrintDisasmSummary(logPath);
            }

            index++;
        }
    }
}
