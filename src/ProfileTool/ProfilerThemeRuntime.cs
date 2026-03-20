using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerThemeRuntime
{
    private readonly string _outputDirectory;
    private readonly Action<string> _writeLine;

    private HotJitDisasmRunner _hotJitDisasmRunner = null!;
    private ProfilerJitModeRunner _jitModeRunner = null!;
    private ProfilerConsoleRenderer _renderer = null!;

    public ProfilerThemeRuntime(string outputDirectory, Theme initialTheme, Action<string> writeLine)
    {
        _outputDirectory = outputDirectory;
        _writeLine = writeLine;
        CurrentTheme = initialTheme;
        Theme.Current = initialTheme;
        RefreshServices();
    }

    public Theme CurrentTheme { get; private set; }

    public HotJitDisasmRunner HotJitDisasmRunner => _hotJitDisasmRunner;

    public ProfilerJitModeRunner JitModeRunner => _jitModeRunner;

    public ProfilerConsoleRenderer Renderer => _renderer;

    public bool TryApply(string? themeName)
    {
        if (!Theme.TryResolve(themeName, out var selectedTheme))
        {
            var name = themeName ?? string.Empty;
            _writeLine($"[{CurrentTheme.ErrorColor}]Unknown theme '{Markup.Escape(name)}'[/]");
            _writeLine($"[{CurrentTheme.AccentColor}]Available themes:[/] {Theme.AvailableThemes}");
            return false;
        }

        CurrentTheme = selectedTheme;
        Theme.Current = selectedTheme;
        RefreshServices();
        return true;
    }

    private void RefreshServices()
    {
        _renderer = new ProfilerConsoleRenderer(CurrentTheme);
        var jitOutputFormatter = new JitOutputFormatter(CurrentTheme);
        var jitCommandRunner = new JitCommandRunner(CurrentTheme, _outputDirectory, jitOutputFormatter);
        var jitContext = new JitExecutionContext(CurrentTheme, jitCommandRunner, jitOutputFormatter, WriteOutputFiles);
        _jitModeRunner = new ProfilerJitModeRunner(jitContext);
        _hotJitDisasmRunner = new HotJitDisasmRunner(jitContext);
    }

    private void WriteOutputFiles(string label, IEnumerable<string> files)
    {
        _writeLine($"[{CurrentTheme.AccentColor}]{label}:[/]");
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }
    }
}
