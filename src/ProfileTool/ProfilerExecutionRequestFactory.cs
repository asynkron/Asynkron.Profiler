using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerExecutionRequestFactory
{
    private readonly Func<Theme> _getTheme;
    private readonly ProjectResolver _projectResolver;

    public ProfilerExecutionRequestFactory(
        Func<Theme> getTheme,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess)
    {
        _getTheme = getTheme;
        _projectResolver = new ProjectResolver(runProcess);
    }

    public ProfilerExecutionRequest? TryCreate(ProfilerCommandInvocation invocation)
    {
        var hasInput = !string.IsNullOrWhiteSpace(invocation.InputPath);
        var hasExplicitModes = invocation.Cpu || invocation.Memory || invocation.Heap || invocation.Contention || invocation.Exception;
        var runCpu = invocation.Cpu || !hasExplicitModes;
        var runMemory = invocation.Memory || !hasExplicitModes;
        var runHeap = invocation.Heap;
        var runException = invocation.Exception;
        var runContention = invocation.Contention;

        if (invocation.JitDisasmHot || invocation.HotThresholdSpecified)
        {
            runCpu = true;
        }

        string label;
        string description;
        string[] command;

        if (hasInput)
        {
            label = ProfileInputCatalog.BuildLabel(invocation.InputPath!);
            description = invocation.InputPath!;
            command = Array.Empty<string>();

            if (!hasExplicitModes)
            {
                var defaultModes = ProfileInputCatalog.GetDefaultModes(invocation.InputPath!);
                runCpu = defaultModes.RunCpu;
                runMemory = defaultModes.RunMemory;
                runHeap = defaultModes.RunHeap;
                runException = defaultModes.RunException;
                runContention = defaultModes.RunContention;
            }
        }
        else
        {
            if (invocation.Command.Length == 0)
            {
                WriteMissingCommand();
                return null;
            }

            var resolved = _projectResolver.Resolve(invocation.Command, invocation.TargetFramework);
            if (resolved == null)
            {
                return null;
            }

            command = resolved.Command;
            label = resolved.Label;
            description = resolved.Description;
        }

        return new ProfilerExecutionRequest(
            label,
            description,
            command,
            invocation.InputPath,
            hasInput,
            hasExplicitModes,
            runCpu,
            runMemory,
            runHeap,
            runException,
            runContention,
            invocation.JitInline,
            invocation.JitDisasm,
            invocation.JitDisasmHot,
            invocation.Jit,
            invocation.HotThresholdSpecified,
            invocation.JitMethod,
            invocation.JitAltJitPath,
            invocation.JitAltJitName,
            BuildRenderRequest(invocation, label, description));
    }

    private void WriteMissingCommand()
    {
        var theme = _getTheme();
        AnsiConsole.MarkupLine($"[{theme.ErrorColor}]No command provided.[/]");
        ProfilerCommandLineHelp.WriteUsageExamples(Console.Out);
    }

    private static ProfileRenderRequest BuildRenderRequest(
        ProfilerCommandInvocation invocation,
        string label,
        string description)
    {
        return new ProfileRenderRequest(
            label,
            description,
            invocation.CallTreeRoot,
            invocation.FunctionFilter,
            invocation.ExceptionTypeFilter,
            invocation.IncludeRuntime,
            invocation.CallTreeDepth,
            invocation.CallTreeWidth,
            invocation.CallTreeRootMode,
            invocation.CallTreeSelf,
            invocation.CallTreeSiblingCutoff,
            invocation.HotThreshold,
            invocation.Timeline,
            invocation.TimelineWidth);
    }
}
