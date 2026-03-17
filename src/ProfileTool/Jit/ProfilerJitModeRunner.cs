using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerJitModeRunner
{
    private readonly JitExecutionContext _jit;

    public ProfilerJitModeRunner(JitExecutionContext jit)
    {
        _jit = jit;
    }

    public bool TryHandleDumpModes(ProfilerExecutionRequest request)
    {
        if (!request.JitInline && !request.JitDisasm)
        {
            return false;
        }

        if (request.HasInput)
        {
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]JIT dump modes require a command, not --input.[/]");
            return true;
        }

        if (request.HasExplicitModes)
        {
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]JIT dump modes cannot be combined with other profiling modes.[/]");
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.JitMethod))
        {
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]Missing --jit-method (e.g. Namespace.Type:Method).[/]");
            return true;
        }

        if (request.JitInline && request.JitDisasm)
        {
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]Choose either --jit-inline or --jit-disasm, not both.[/]");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.JitAltJitPath) && !File.Exists(request.JitAltJitPath))
        {
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]AltJit path not found:[/] {Markup.Escape(request.JitAltJitPath)}");
            return true;
        }

        var dumpFiles = request.JitInline
            ? _jit.CommandRunner.RunInlineDump(request.Command, request.JitMethod!, request.JitAltJitPath, request.JitAltJitName)
            : _jit.CommandRunner.RunDisasm(request.Command, request.JitMethod!);
        _jit.WriteOutputFiles(request.JitInline ? "JIT inline dump files" : "JIT disasm files", dumpFiles);

        var logPath = JitCommandRunner.GetPrimaryLogPath(dumpFiles);
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            if (request.JitInline)
            {
                _jit.OutputFormatter.PrintInlineSummary(logPath);
            }
            else
            {
                _jit.OutputFormatter.PrintDisasmSummary(logPath);
            }
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
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]Hot JIT disasm requires a command, not --input.[/]");
            return false;
        }

        if (request.JitInline || request.JitDisasm)
        {
            AnsiConsole.MarkupLine($"[{_jit.Theme.ErrorColor}]Hot JIT disasm cannot be combined with JIT dump modes.[/]");
            return false;
        }

        return true;
    }
}
