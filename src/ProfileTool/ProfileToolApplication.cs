using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Linq;

namespace Asynkron.Profiler;

internal sealed class ProfileToolApplication
{
    private readonly ProfileCommandHandler _handler = new();

    public Task<int> InvokeAsync(string[] args)
    {
        var options = new ProfileCommandOptions();
        var rootCommand = CreateRootCommand(options);
        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseHelpBuilder(_ =>
            {
                var helpBuilder = new HelpBuilder(LocalizationResources.Instance, _handler.GetHelpWidth());
                helpBuilder.CustomizeLayout(context =>
                    HelpBuilder.Default.GetLayout()
                        .Concat(new HelpSectionDelegate[] { helpContext => _handler.WriteExamplesSection(helpContext, rootCommand) }));
                return helpBuilder;
            })
            .Build();

        return parser.InvokeAsync(args);
    }

    private RootCommand CreateRootCommand(ProfileCommandOptions options)
    {
        var rootCommand = new RootCommand("Asynkron Profiler - CPU/Memory/Exception/Contention/Heap profiling for .NET commands");
        options.AddTo(rootCommand);
        rootCommand.TreatUnmatchedTokensAsErrors = false;
        rootCommand.SetHandler(context => _handler.Handle(context, options));
        return rootCommand;
    }
}
