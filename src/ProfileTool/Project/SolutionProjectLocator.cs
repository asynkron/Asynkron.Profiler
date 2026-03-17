using System.Text.RegularExpressions;

namespace Asynkron.Profiler;

internal static partial class SolutionProjectLocator
{
    public static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        var fullSolutionPath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(fullSolutionPath);
        if (solutionDirectory == null)
        {
            return [];
        }

        return File.ReadLines(fullSolutionPath)
            .Select(TryGetProjectPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, path!)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryGetProjectPath(string line)
    {
        var match = ProjectLinePattern().Match(line);
        return match.Success ? NormalizeSolutionPath(match.Groups[1].Value) : null;
    }

    private static string NormalizeSolutionPath(string solutionPath)
    {
        return solutionPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    [GeneratedRegex("Project\\([^)]*\\)\\s*=\\s*\"[^\"]+\",\\s*\"([^\"]+\\.csproj)\"", RegexOptions.IgnoreCase, "sv-SE")]
    private static partial Regex ProjectLinePattern();
}
