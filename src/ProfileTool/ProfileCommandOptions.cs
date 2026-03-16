using System.CommandLine;
using System.Globalization;

namespace Asynkron.Profiler;

internal sealed class ProfileCommandOptions
{
    public const double DefaultHotnessThreshold = 0.4d;

    public ProfileCommandOptions()
    {
        Exception.AddAlias("--exceptions");
        Theme.AddAlias("-t");
        Command.Arity = ArgumentArity.ZeroOrMore;
    }

    public Option<bool> Cpu { get; } = new("--cpu", "Run CPU profiling only");
    public Option<bool> Timeline { get; } = new("--timeline", "Show inline timeline bars in call tree (use with --cpu)");
    public Option<int> TimelineWidth { get; } = new("--timeline-width", () => 40, "Timeline bar width in characters (default: 40)");
    public Option<bool> Memory { get; } = new("--memory", "Run memory profiling only");
    public Option<bool> Exception { get; } = new("--exception", "Run exception profiling only");
    public Option<bool> Contention { get; } = new("--contention", "Run lock contention profiling only");
    public Option<bool> Heap { get; } = new("--heap", "Capture heap snapshot");
    public Option<bool> JitInline { get; } = new("--jit-inline", "Capture JIT inlining dumps to files (no parsing)");
    public Option<bool> JitDisasm { get; } = new("--jit-disasm", "Capture JIT disassembly output to files (no parsing)");
    public Option<bool> JitDisasmHot { get; } = new("--jit-disasm-hot", "Capture JIT disassembly for hot methods after CPU profiling");
    public Option<bool> Jit { get; } = new("--jit", "Enable JIT decompilation for hot methods (requires --hot)");
    public Option<string?> JitMethod { get; } = new("--jit-method", "Method filter for JIT dumps (e.g. Namespace.Type:Method)");
    public Option<string?> JitAltJitPath { get; } = new("--jit-altjit-path", "Path to a Debug/Checked JIT (libclrjit) for JitDump");
    public Option<string?> JitAltJitName { get; } = new("--jit-altjit-name", () => "clrjit", "AltJit name (default: clrjit)");
    public Option<string?> CallTreeRoot { get; } = new("--root", "Filter call tree to a root method (substring match)");
    public Option<int> CallTreeDepth { get; } = new("--calltree-depth", () => 30, "Maximum call tree depth (default: 30)");
    public Option<int> CallTreeWidth { get; } = new("--calltree-width", () => 4, "Maximum children per node (default: 4)");
    public Option<string?> CallTreeRootMode { get; } = new("--root-mode", () => "hottest", "Root selection mode when multiple matches (hottest|shallowest|first)");
    public Option<bool> CallTreeSelf { get; } = new("--calltree-self", "Show self-time call tree in addition to total time");
    public Option<int> CallTreeSiblingCutoff { get; } = new("--calltree-sibling-cutoff", () => 5, "Hide siblings below X% of the top sibling (default: 5)");
    public Option<string?> HotThreshold { get; } = new(
        "--hot",
        () => DefaultHotnessThreshold.ToString(CultureInfo.InvariantCulture),
        "Hotness threshold for hotspot markers/JIT disasm (0-1)");
    public Option<string?> FunctionFilter { get; } = new("--filter", "Filter CPU function tables by substring (case-insensitive)");
    public Option<string?> ExceptionType { get; } = new("--exception-type", "Filter exception tables and call trees by exception type (substring match)");
    public Option<bool> IncludeRuntime { get; } = new("--include-runtime", "Include runtime/process frames in CPU tables and call tree");
    public Option<string?> Input { get; } = new("--input", "Render results from an existing trace file");
    public Option<string?> TargetFramework { get; } = new("--tfm", "Target framework to use for .csproj/.sln inputs (e.g. net8.0)");
    public Option<string?> Theme { get; } = new("--theme", "Color theme (default|onedark|dracula|nord|monokai)");
    public Argument<string[]> Command { get; } = new("command", () => Array.Empty<string>(), "Command to profile (pass after --)");

    public void AddTo(RootCommand rootCommand)
    {
        rootCommand.AddOption(Cpu);
        rootCommand.AddOption(Timeline);
        rootCommand.AddOption(TimelineWidth);
        rootCommand.AddOption(Memory);
        rootCommand.AddOption(Exception);
        rootCommand.AddOption(Contention);
        rootCommand.AddOption(Heap);
        rootCommand.AddOption(JitInline);
        rootCommand.AddOption(JitDisasm);
        rootCommand.AddOption(JitDisasmHot);
        rootCommand.AddOption(Jit);
        rootCommand.AddOption(JitMethod);
        rootCommand.AddOption(JitAltJitPath);
        rootCommand.AddOption(JitAltJitName);
        rootCommand.AddOption(CallTreeRoot);
        rootCommand.AddOption(CallTreeDepth);
        rootCommand.AddOption(CallTreeWidth);
        rootCommand.AddOption(CallTreeRootMode);
        rootCommand.AddOption(CallTreeSelf);
        rootCommand.AddOption(CallTreeSiblingCutoff);
        rootCommand.AddOption(HotThreshold);
        rootCommand.AddOption(FunctionFilter);
        rootCommand.AddOption(ExceptionType);
        rootCommand.AddOption(IncludeRuntime);
        rootCommand.AddOption(Input);
        rootCommand.AddOption(TargetFramework);
        rootCommand.AddOption(Theme);
        rootCommand.AddArgument(Command);
    }
}
