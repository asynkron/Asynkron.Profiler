using Spectre.Console;

namespace Asynkron.Profiler;

internal static class ProjectLaunchResolver
{
    public static string? ResolveTargetFramework(
        ProjectMetadataSnapshot metadata,
        string? targetFramework,
        string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            return targetFramework;
        }

        if (!string.IsNullOrWhiteSpace(metadata.TargetFrameworks))
        {
            var frameworks = metadata.TargetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (frameworks.Count == 1)
            {
                return frameworks[0];
            }

            AnsiConsole.MarkupLine($"[red]Multiple target frameworks found for:[/] {Markup.Escape(projectPath)}");
            AnsiConsole.MarkupLine($"[dim]Use --tfm <tfm> to select one: {Markup.Escape(metadata.TargetFrameworks)}[/]");
            return null;
        }

        return metadata.TargetFramework;
    }

    public static string[] ResolveTargetCommand(string targetPath)
    {
        return targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? ["dotnet", targetPath]
            : [targetPath];
    }
}
