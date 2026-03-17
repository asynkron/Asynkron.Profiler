using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed class JitExecutionContext
{
    public JitExecutionContext(
        Theme theme,
        JitCommandRunner commandRunner,
        JitOutputFormatter outputFormatter,
        Action<string, IEnumerable<string>> writeOutputFiles)
    {
        Theme = theme;
        CommandRunner = commandRunner;
        OutputFormatter = outputFormatter;
        WriteOutputFiles = writeOutputFiles;
    }

    public Theme Theme { get; }

    public JitCommandRunner CommandRunner { get; }

    public JitOutputFormatter OutputFormatter { get; }

    public Action<string, IEnumerable<string>> WriteOutputFiles { get; }
}
