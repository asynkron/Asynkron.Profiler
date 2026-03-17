using System.CommandLine;
using System.CommandLine.Parsing;
using Asynkron.Profiler;
using Spectre.Console;

ConfigureConsoleForRedirectedOutput();

var commandLine = new ProfilerCommandLine();
var executor = new ProfilerCommandExecutor(Environment.CurrentDirectory);

commandLine.RootCommand.SetHandler(context =>
{
    if (!commandLine.TryCreateInvocation(context.ParseResult, out var invocation, out var errorMessage))
    {
        var message = errorMessage ?? "Invalid command line arguments.";
        AnsiConsole.MarkupLine($"[{Theme.Current.ErrorColor}]{Markup.Escape(message)}[/]");
        return;
    }

    executor.Execute(invocation);
});

return await commandLine.BuildParser().InvokeAsync(args);

static void ConfigureConsoleForRedirectedOutput()
{
    if (!Console.IsOutputRedirected)
    {
        return;
    }

    var capabilities = AnsiConsole.Profile.Capabilities;
    capabilities.Ansi = false;
    capabilities.Unicode = false;
    capabilities.Links = false;
    capabilities.Interactive = false;
    AnsiConsole.Profile.Capabilities = capabilities;
    AnsiConsole.Profile.Width = 200;
}
