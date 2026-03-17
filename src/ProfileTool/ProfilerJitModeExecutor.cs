using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerJitModeExecutor
{
    private readonly Func<Theme> _getTheme;
    private readonly JitCommandRunner _jitCommandRunner;
    private readonly JitOutputFormatter _jitOutputFormatter;
    private readonly Action<string, IEnumerable<string>> _writeOutputFiles;

    public ProfilerJitModeExecutor(
        Func<Theme> getTheme,
        JitCommandRunner jitCommandRunner,
        JitOutputFormatter jitOutputFormatter,
        Action<string, IEnumerable<string>> writeOutputFiles)
    {
        _getTheme = getTheme;
        _jitCommandRunner = jitCommandRunner;
        _jitOutputFormatter = jitOutputFormatter;
        _writeOutputFiles = writeOutputFiles;
    }

    public bool TryHandle(ProfilerExecutionRequest request)
    {
        if (!request.JitInline && !request.JitDisasm)
        {
            return false;
        }

        if (request.HasInput)
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]JIT dump modes require a command, not --input.[/]");
            return true;
        }

        if (request.HasExplicitModes)
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]JIT dump modes cannot be combined with other profiling modes.[/]");
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.JitMethod))
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]Missing --jit-method (e.g. Namespace.Type:Method).[/]");
            return true;
        }

        if (request.JitInline && request.JitDisasm)
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]Choose either --jit-inline or --jit-disasm, not both.[/]");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.JitAltJitPath) && !File.Exists(request.JitAltJitPath))
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]AltJit path not found:[/] {Markup.Escape(request.JitAltJitPath)}");
            return true;
        }

        var dumpFiles = request.JitInline
            ? _jitCommandRunner.RunInlineDump(request.Command, request.JitMethod!, request.JitAltJitPath, request.JitAltJitName)
            : _jitCommandRunner.RunDisasm(request.Command, request.JitMethod!);
        _writeOutputFiles(request.JitInline ? "JIT inline dump files" : "JIT disasm files", dumpFiles);

        var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return true;
        }

        if (request.JitInline)
        {
            _jitOutputFormatter.PrintInlineSummary(logPath);
        }
        else
        {
            _jitOutputFormatter.PrintDisasmSummary(logPath);
        }

        return true;
    }

    public bool ValidateHotJitRequest(ProfilerExecutionRequest request)
    {
        if (!request.HasHotJitRequest)
        {
            return true;
        }

        if (request.HasInput)
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]Hot JIT disasm requires a command, not --input.[/]");
            return false;
        }

        if (request.JitInline || request.JitDisasm)
        {
            AnsiConsole.MarkupLine($"[{_getTheme().ErrorColor}]Hot JIT disasm cannot be combined with JIT dump modes.[/]");
            return false;
        }

        return true;
    }
}
