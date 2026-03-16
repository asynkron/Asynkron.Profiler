using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class JitCommandRunner
{
    private readonly Theme _theme;
    private readonly string _outputDirectory;
    private readonly JitOutputFormatter _outputFormatter;

    public JitCommandRunner(Theme theme, string outputDirectory, JitOutputFormatter outputFormatter)
    {
        _theme = theme;
        _outputDirectory = outputDirectory;
        _outputFormatter = outputFormatter;
    }

    public string[] RunInlineDump(
        string[] command,
        string jitMethod,
        string? jitAltJitPath,
        string? jitAltJitName)
    {
        if (command.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No command provided for JIT inline dump.[/]");
            return Array.Empty<string>();
        }

        var existing = new HashSet<string>(
            Directory.GetFiles(_outputDirectory, "jitdump.*.txt"),
            StringComparer.OrdinalIgnoreCase);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var stdoutFile = Path.Combine(_outputDirectory, $"jitdump_{timestamp}.log");
        var stderrFile = Path.Combine(_outputDirectory, $"jitdump_{timestamp}.err.log");

        AnsiConsole.MarkupLine($"[dim]Capturing JIT inlining dumps for {Markup.Escape(jitMethod)}...[/]");

        var psi = CreateProcessStartInfo(command);
        psi.Environment["COMPlus_JitDump"] = jitMethod;
        psi.Environment["COMPlus_JitDumpInlinePhases"] = "1";
        psi.Environment["COMPlus_JitDumpASCII"] = "0";
        psi.Environment["COMPlus_TieredCompilation"] = "0";
        psi.Environment["COMPlus_ReadyToRun"] = "0";
        psi.Environment["COMPlus_ZapDisable"] = "1";
        psi.Environment["DOTNET_JitDump"] = jitMethod;
        psi.Environment["DOTNET_JitDumpInlinePhases"] = "1";
        psi.Environment["DOTNET_JitDumpASCII"] = "0";
        psi.Environment["DOTNET_TieredCompilation"] = "0";
        psi.Environment["DOTNET_ReadyToRun"] = "0";
        psi.Environment["DOTNET_ZapDisable"] = "1";
        if (!string.IsNullOrWhiteSpace(jitAltJitPath))
        {
            var altJitName = string.IsNullOrWhiteSpace(jitAltJitName) ? "clrjit" : jitAltJitName;
            psi.Environment["COMPlus_AltJit"] = jitMethod;
            psi.Environment["COMPlus_AltJitName"] = altJitName;
            psi.Environment["COMPlus_AltJitPath"] = jitAltJitPath;
            psi.Environment["DOTNET_AltJit"] = jitMethod;
            psi.Environment["DOTNET_AltJitName"] = altJitName;
            psi.Environment["DOTNET_AltJitPath"] = jitAltJitPath;
        }

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Failed to start process for JIT inline dump.[/]");
            return Array.Empty<string>();
        }

        using var stdoutWriter = new StreamWriter(stdoutFile, append: false, Encoding.UTF8);
        using var stderrWriter = new StreamWriter(stderrFile, append: false, Encoding.UTF8);
        stdoutWriter.AutoFlush = true;
        stderrWriter.AutoFlush = true;

        ProcessRunner.AttachDataHandlers(proc, stdoutWriter.WriteLine, stderrWriter.WriteLine);

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]JIT dump process exited with code {proc.ExitCode}.[/]");
        }

        var results = new List<string>
        {
            Path.GetFullPath(stdoutFile)
        };
        AppendIfNotEmpty(results, stderrFile);
        results.AddRange(Directory.GetFiles(_outputDirectory, "jitdump.*.txt")
            .Where(file => !existing.Contains(file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath));

        var hasJitDumpMarkers = File.ReadLines(stdoutFile)
            .Any(line => line.Contains("JIT compiling", StringComparison.Ordinal));
        if (!hasJitDumpMarkers)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No JIT dump markers found. This usually means a Debug/Checked JIT is required.[/]");
        }

        return results.ToArray();
    }

    public string[] RunDisasm(string[] command, string jitMethod, bool suppressNoMarkersWarning = false)
    {
        if (command.Length == 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No command provided for JIT disasm.[/]");
            return Array.Empty<string>();
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var stdoutFile = Path.Combine(_outputDirectory, $"jitdisasm_{timestamp}.log");
        var stderrFile = Path.Combine(_outputDirectory, $"jitdisasm_{timestamp}.err.log");
        var colorFile = Path.Combine(_outputDirectory, $"jitdisasm_{timestamp}.color.log");

        AnsiConsole.MarkupLine($"[dim]Capturing JIT disassembly for {Markup.Escape(jitMethod)}...[/]");

        var psi = CreateProcessStartInfo(command);
        psi.Environment["COMPlus_JitDisasm"] = jitMethod;
        psi.Environment["COMPlus_TieredCompilation"] = "0";
        psi.Environment["COMPlus_ReadyToRun"] = "0";
        psi.Environment["COMPlus_ZapDisable"] = "1";
        psi.Environment["DOTNET_JitDisasm"] = jitMethod;
        psi.Environment["DOTNET_TieredCompilation"] = "0";
        psi.Environment["DOTNET_ReadyToRun"] = "0";
        psi.Environment["DOTNET_ZapDisable"] = "1";

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]Failed to start process for JIT disasm.[/]");
            return Array.Empty<string>();
        }

        using var stdoutWriter = new StreamWriter(stdoutFile, append: false, Encoding.UTF8);
        using var stderrWriter = new StreamWriter(stderrFile, append: false, Encoding.UTF8);
        using var colorWriter = new StreamWriter(colorFile, append: false, Encoding.UTF8);
        stdoutWriter.AutoFlush = true;
        stderrWriter.AutoFlush = true;
        colorWriter.AutoFlush = true;

        ProcessRunner.AttachDataHandlers(
            proc,
            data =>
            {
                stdoutWriter.WriteLine(data);
                colorWriter.WriteLine(_outputFormatter.ColorizeDisasmLine(data));
            },
            stderrWriter.WriteLine);

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]JIT disasm process exited with code {proc.ExitCode}.[/]");
        }

        var hasDisasmMarkers = HasDisasmMarkers(stdoutFile);
        if (!hasDisasmMarkers && !suppressNoMarkersWarning)
        {
            AnsiConsole.MarkupLine($"[{_theme.ErrorColor}]No JIT disassembly markers found. Check the method filter.[/]");
        }

        var results = new List<string>
        {
            Path.GetFullPath(stdoutFile),
            Path.GetFullPath(colorFile)
        };
        AppendIfNotEmpty(results, stderrFile);
        return results.ToArray();
    }

    public static bool HasDisasmMarkers(string logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return false;
        }

        return File.ReadLines(logPath)
            .Any(line => line.StartsWith("; Assembly listing for method", StringComparison.Ordinal));
    }

    public static string? GetPrimaryLogPath(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (file.EndsWith(".err.log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private ProcessStartInfo CreateProcessStartInfo(string[] command)
    {
        return CommandProcessStartInfoFactory.Create(command, _outputDirectory);
    }

    private static void AppendIfNotEmpty(List<string> results, string path)
    {
        if (new FileInfo(path).Length > 0)
        {
            results.Add(Path.GetFullPath(path));
        }
    }
}
