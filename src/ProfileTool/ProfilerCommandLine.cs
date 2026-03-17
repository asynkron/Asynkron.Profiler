using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Linq;

namespace Asynkron.Profiler;

internal sealed class ProfilerCommandLine
{
    private const double DefaultHotThreshold = 0.4d;

    private readonly Option<bool> _cpuOption = new("--cpu", "Run CPU profiling only");
    private readonly Option<bool> _timelineOption = new("--timeline", "Show inline timeline bars in call tree (use with --cpu)");
    private readonly Option<int> _timelineWidthOption = new("--timeline-width", () => 40, "Timeline bar width in characters (default: 40)");
    private readonly Option<bool> _memoryOption = new("--memory", "Run memory profiling only");
    private readonly Option<bool> _exceptionOption = new("--exception", "Run exception profiling only");
    private readonly Option<bool> _contentionOption = new("--contention", "Run lock contention profiling only");
    private readonly Option<bool> _heapOption = new("--heap", "Capture heap snapshot");
    private readonly Option<bool> _jitInlineOption = new("--jit-inline", "Capture JIT inlining dumps to files (no parsing)");
    private readonly Option<bool> _jitDisasmOption = new("--jit-disasm", "Capture JIT disassembly output to files (no parsing)");
    private readonly Option<bool> _jitDisasmHotOption = new("--jit-disasm-hot", "Capture JIT disassembly for hot methods after CPU profiling");
    private readonly Option<bool> _jitOption = new("--jit", "Enable JIT decompilation for hot methods (requires --hot)");
    private readonly Option<string?> _jitMethodOption = new("--jit-method", "Method filter for JIT dumps (e.g. Namespace.Type:Method)");
    private readonly Option<string?> _jitAltJitPathOption = new("--jit-altjit-path", "Path to a Debug/Checked JIT (libclrjit) for JitDump");
    private readonly Option<string?> _jitAltJitNameOption = new("--jit-altjit-name", () => "clrjit", "AltJit name (default: clrjit)");
    private readonly Option<string?> _callTreeRootOption = new("--root", "Filter call tree to a root method (substring match)");
    private readonly Option<int> _callTreeDepthOption = new("--calltree-depth", () => 30, "Maximum call tree depth (default: 30)");
    private readonly Option<int> _callTreeWidthOption = new("--calltree-width", () => 4, "Maximum children per node (default: 4)");
    private readonly Option<string?> _callTreeRootModeOption = new("--root-mode", () => "hottest", "Root selection mode when multiple matches (hottest|shallowest|first)");
    private readonly Option<bool> _callTreeSelfOption = new("--calltree-self", "Show self-time call tree in addition to total time");
    private readonly Option<int> _callTreeSiblingCutoffOption = new("--calltree-sibling-cutoff", () => 5, "Hide siblings below X% of the top sibling (default: 5)");
    private readonly Option<string?> _hotThresholdOption = new(
        "--hot",
        () => DefaultHotThreshold.ToString(CultureInfo.InvariantCulture),
        "Hotness threshold for hotspot markers/JIT disasm (0-1)");
    private readonly Option<string?> _functionFilterOption = new("--filter", "Filter CPU function tables by substring (case-insensitive)");
    private readonly Option<string?> _exceptionTypeOption = new("--exception-type", "Filter exception tables and call trees by exception type (substring match)");
    private readonly Option<bool> _includeRuntimeOption = new("--include-runtime", "Include runtime/process frames in CPU tables and call tree");
    private readonly Option<string?> _inputOption = new("--input", "Render results from an existing trace file");
    private readonly Option<string?> _targetFrameworkOption = new("--tfm", "Target framework to use for .csproj/.sln inputs (e.g. net8.0)");
    private readonly Option<string?> _themeOption = new("--theme", "Color theme (default|onedark|dracula|nord|monokai)");
    private readonly Argument<string[]> _commandArgument = new("command", () => Array.Empty<string>(), "Command to profile (pass after --)");

    public ProfilerCommandLine()
    {
        _exceptionOption.AddAlias("--exceptions");
        _themeOption.AddAlias("-t");
        _commandArgument.Arity = ArgumentArity.ZeroOrMore;

        RootCommand = new RootCommand("Asynkron Profiler - CPU/Memory/Exception/Contention/Heap profiling for .NET commands")
        {
            _cpuOption,
            _timelineOption,
            _timelineWidthOption,
            _memoryOption,
            _exceptionOption,
            _contentionOption,
            _heapOption,
            _jitInlineOption,
            _jitDisasmOption,
            _jitDisasmHotOption,
            _jitOption,
            _jitMethodOption,
            _jitAltJitPathOption,
            _jitAltJitNameOption,
            _callTreeRootOption,
            _callTreeDepthOption,
            _callTreeWidthOption,
            _callTreeRootModeOption,
            _callTreeSelfOption,
            _callTreeSiblingCutoffOption,
            _hotThresholdOption,
            _functionFilterOption,
            _exceptionTypeOption,
            _includeRuntimeOption,
            _inputOption,
            _targetFrameworkOption,
            _themeOption,
            _commandArgument
        };

        RootCommand.TreatUnmatchedTokensAsErrors = false;
    }

    public RootCommand RootCommand { get; }

    public Parser BuildParser()
    {
        return new CommandLineBuilder(RootCommand)
            .UseDefaults()
            .UseHelpBuilder(_ =>
            {
                var helpBuilder = new HelpBuilder(LocalizationResources.Instance, ProfilerCommandLineHelp.GetHelpWidth());
                helpBuilder.CustomizeLayout(context =>
                    HelpBuilder.Default.GetLayout().Concat(new HelpSectionDelegate[] { AppendExamplesSection }));
                return helpBuilder;
            })
            .Build();
    }

    public bool TryCreateInvocation(
        ParseResult parseResult,
        out ProfilerCommandInvocation invocation,
        out string? errorMessage)
    {
        var hotThresholdInput = parseResult.GetValueForOption(_hotThresholdOption);
        if (!TryParseHotThreshold(hotThresholdInput, out var hotThreshold))
        {
            invocation = null!;
            errorMessage = "--hot must be a number between 0 and 1 (use 0.3 or 0,3).";
            return false;
        }

        invocation = new ProfilerCommandInvocation(
            parseResult.GetValueForOption(_cpuOption),
            parseResult.GetValueForOption(_timelineOption),
            parseResult.GetValueForOption(_timelineWidthOption),
            parseResult.GetValueForOption(_memoryOption),
            parseResult.GetValueForOption(_exceptionOption),
            parseResult.GetValueForOption(_contentionOption),
            parseResult.GetValueForOption(_heapOption),
            parseResult.GetValueForOption(_jitInlineOption),
            parseResult.GetValueForOption(_jitDisasmOption),
            parseResult.GetValueForOption(_jitDisasmHotOption),
            parseResult.GetValueForOption(_jitOption),
            parseResult.GetValueForOption(_jitMethodOption),
            parseResult.GetValueForOption(_jitAltJitPathOption),
            parseResult.GetValueForOption(_jitAltJitNameOption),
            parseResult.GetValueForOption(_callTreeRootOption),
            parseResult.GetValueForOption(_callTreeDepthOption),
            parseResult.GetValueForOption(_callTreeWidthOption),
            parseResult.GetValueForOption(_callTreeRootModeOption),
            parseResult.GetValueForOption(_callTreeSelfOption),
            parseResult.GetValueForOption(_callTreeSiblingCutoffOption),
            hotThreshold,
            parseResult.FindResultFor(_hotThresholdOption) != null,
            parseResult.GetValueForOption(_functionFilterOption),
            parseResult.GetValueForOption(_exceptionTypeOption),
            parseResult.GetValueForOption(_includeRuntimeOption),
            parseResult.GetValueForOption(_inputOption),
            parseResult.GetValueForOption(_targetFrameworkOption),
            parseResult.GetValueForOption(_themeOption),
            parseResult.GetValueForArgument(_commandArgument) ?? Array.Empty<string>());
        errorMessage = null;
        return true;
    }

    private void AppendExamplesSection(HelpContext context)
    {
        if (!ReferenceEquals(context.Command, RootCommand))
        {
            return;
        }

        context.Output.WriteLine();
        ProfilerCommandLineHelp.WriteUsageExamples(context.Output);
    }

    private static bool TryParseHotThreshold(string? input, out double value)
    {
        value = DefaultHotThreshold;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var trimmed = input.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return value is >= 0d and <= 1d;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return value is >= 0d and <= 1d;
        }

        return false;
    }
}
