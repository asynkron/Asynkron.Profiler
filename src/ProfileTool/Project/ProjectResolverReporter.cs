using Spectre.Console;

namespace Asynkron.Profiler;

internal static class ProjectResolverReporter
{
    public static void WriteInvalidSolutionPath(string solutionPath)
    {
        AnsiConsole.MarkupLine($"[red]Invalid solution path:[/] {Markup.Escape(solutionPath)}");
    }

    public static void WriteBuildFailed(string error)
    {
        AnsiConsole.MarkupLine($"[red]Build failed:[/] {Markup.Escape(error)}");
    }

    public static void WriteNoProjectsInSolution(string solutionPath)
    {
        AnsiConsole.MarkupLine($"[red]No projects found in solution:[/] {Markup.Escape(solutionPath)}");
    }

    public static void WriteExecutableProjectsNotResolved(IReadOnlyList<string> projects)
    {
        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No executable projects found in solution.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Multiple executable projects found in solution.[/]");
        }

        foreach (var project in projects)
        {
            AnsiConsole.MarkupLine($"[dim]- {Markup.Escape(project)}[/]");
        }

        AnsiConsole.MarkupLine("[dim]Pass a .csproj path directly to profile a specific project.[/]");
    }

    public static void WriteProjectNotFound(string projectPath)
    {
        AnsiConsole.MarkupLine($"[red]Project not found:[/] {Markup.Escape(projectPath)}");
    }

    public static void WriteProjectNotExecutable(string projectPath)
    {
        AnsiConsole.MarkupLine($"[red]Project is not executable:[/] {Markup.Escape(projectPath)}");
    }

    public static void WriteTargetPathNotFound(string projectPath)
    {
        AnsiConsole.MarkupLine($"[red]Could not determine output path for:[/] {Markup.Escape(projectPath)}");
    }
}
