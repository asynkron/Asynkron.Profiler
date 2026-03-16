using System;
using System.Collections.Generic;
using System.IO;

namespace Asynkron.Profiler;

internal static class ProfileToolHelp
{
    public static IReadOnlyList<string> GetUsageExampleLines()
    {
        return
        [
            "Examples:",
            "",
            "CPU profiling:",
            "  asynkron-profiler --cpu -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --cpu --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --cpu --calltree-depth 5 -- ./MyApp.csproj",
            "  asynkron-profiler --cpu --calltree-depth 5 -- ./MySolution.sln",
            "  asynkron-profiler --cpu --input ./profile-output/app.speedscope.json",
            "  asynkron-profiler --cpu --timeline -- ./bin/Release/<tfm>/MyApp",
            "",
            "Memory profiling:",
            "  asynkron-profiler --memory -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --memory --root \"MyNamespace\" -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --memory --input ./profile-output/app.nettrace",
            "",
            "Exception profiling:",
            "  asynkron-profiler --exception -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --exception --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --exception --exception-type \"InvalidOperation\" -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --exception --input ./profile-output/app.nettrace",
            "",
            "Lock contention profiling:",
            "  asynkron-profiler --contention -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --contention --calltree-depth 5 -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --contention --input ./profile-output/app.nettrace",
            "",
            "Heap snapshot:",
            "  asynkron-profiler --heap -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --heap --input ./profile-output/app.gcdump",
            "",
            "JIT inlining dumps:",
            "  asynkron-profiler --jit-inline --jit-method \"Namespace.Type:Method\" -- ./bin/Release/<tfm>/MyApp",
            "  asynkron-profiler --jit-inline --jit-method \"Namespace.Type:Method\" --jit-altjit-path /path/to/libclrjit.dylib -- ./bin/Release/<tfm>/MyApp",
            "JIT disassembly:",
            "  asynkron-profiler --jit-disasm --jit-method \"Namespace.Type:Method\" -- ./bin/Release/<tfm>/MyApp",
            "",
            "Render existing traces:",
            "  asynkron-profiler --input ./profile-output/app.nettrace",
            "  asynkron-profiler --input ./profile-output/app.speedscope.json --cpu",
            "  asynkron-profiler --input ./profile-output/app.etlx --memory",
            "  asynkron-profiler --input ./profile-output/app.nettrace --exception",
            "",
            "General:",
            "  asynkron-profiler --help",
            "",
            "Themes:",
            "  asynkron-profiler --theme onedark --cpu -- ./bin/Release/<tfm>/MyApp",
            $"  Available: {Theme.AvailableThemes}"
        ];
    }

    public static void WriteUsageExamples(TextWriter writer)
    {
        foreach (var line in GetUsageExampleLines())
        {
            writer.WriteLine(line);
        }
    }

    public static int GetHelpWidth()
    {
        if (Console.IsOutputRedirected)
        {
            return 200;
        }

        try
        {
            return Math.Max(80, Console.WindowWidth);
        }
        catch
        {
            return 120;
        }
    }
}
