using System.Text.Json;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class MsbuildProjectMetadataReader
{
    private readonly Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> _runProcess;

    public MsbuildProjectMetadataReader(
        Func<string, IEnumerable<string>, string?, int, (bool Success, string StdOut, string StdErr)> runProcess)
    {
        _runProcess = runProcess;
    }

    public ProjectMetadataSnapshot? Read(string projectPath, string? targetFramework)
    {
        var args = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:OutputType",
            "-getProperty:TargetPath",
            "-property:Configuration=Release"
        };

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            args.Add($"-property:TargetFramework={targetFramework}");
        }

        var (success, stdout, stderr) = _runProcess("dotnet", args, null, 600000);
        if (!success)
        {
            AnsiConsole.MarkupLine($"[red]Failed to query project metadata:[/] {Markup.Escape(stderr)}");
            return null;
        }

        var properties = ParseProperties(stdout);
        return new ProjectMetadataSnapshot(
            GetProperty(properties, "TargetFramework"),
            GetProperty(properties, "TargetFrameworks"),
            GetProperty(properties, "OutputType"),
            GetProperty(properties, "TargetPath"));
    }

    private static Dictionary<string, string> ParseProperties(string stdout)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = stdout.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("Properties", out var props))
                {
                    foreach (var property in props.EnumerateObject())
                    {
                        if (!string.IsNullOrWhiteSpace(property.Name))
                        {
                            result[property.Name] = property.Value.GetString() ?? string.Empty;
                        }
                    }

                    return result;
                }
            }
            catch
            {
                // Fall back to line parsing if JSON parsing fails.
            }
        }

        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string GetProperty(Dictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out var value) ? value : string.Empty;
    }
}

internal sealed record ProjectMetadataSnapshot(
    string TargetFramework,
    string TargetFrameworks,
    string OutputType,
    string TargetPath);
