using System;
using System.Collections.Generic;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerToolAvailability
{
    private static readonly string[] VersionProbeArgs = ["--version"];

    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<Theme> _getTheme;
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;
    private readonly Action<string> _writeLine;

    public ProfilerToolAvailability(
        Func<Theme> getTheme,
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess,
        Action<string> writeLine)
    {
        _getTheme = getTheme;
        _runProcess = runProcess;
        _writeLine = writeLine;
    }

    public bool EnsureAvailable(string toolName, string installHint)
    {
        if (_cache.TryGetValue(toolName, out var cached))
        {
            return cached;
        }

        var (success, _, stderr) = _runProcess(toolName, VersionProbeArgs, null, 10000);
        if (!success)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? "Tool not found." : stderr.Trim();
            var theme = _getTheme();
            _writeLine($"[{theme.ErrorColor}]{toolName} unavailable:[/] {Markup.Escape(detail)}");
            _writeLine($"[{theme.AccentColor}]Install:[/] {Markup.Escape(installHint)}");
            _cache[toolName] = false;
            return false;
        }

        _cache[toolName] = true;
        return true;
    }
}
