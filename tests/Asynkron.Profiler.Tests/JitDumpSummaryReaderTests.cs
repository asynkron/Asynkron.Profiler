using System;
using System.IO;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class JitDumpSummaryReaderTests
{
    [Fact]
    public void ReadDisasmSummaryParsesMethodMetadataAndInstructionCounts()
    {
        const string logContent = """
            ; Assembly listing for method Namespace.Type:Method() (Tier1)
            ; Emitting BLENDED_CODE for X64
            ; 2 inlinees with PGO data; 1 single block inlinees; 3 inlinees without PGO data
            G_M000_IG01:
                   mov      eax, 1
                   add      eax, 2
            G_M000_IG02:
                   ret
            ; code size 42
            """;

        var logPath = CreateTempFile(logContent);

        try
        {
            var summary = JitDumpSummaryReader.ReadDisasmSummary(logPath);

            Assert.NotNull(summary);
            Assert.Equal("Namespace.Type:Method()", summary!.MethodName);
            Assert.Equal("Tier1", summary.Tier);
            Assert.Equal("Emitting BLENDED_CODE for X64", summary.Target);
            Assert.Equal("2 inlinees with PGO data; 1 single block inlinees; 3 inlinees without PGO data", summary.PgoLine);
            Assert.Equal(2, summary.InlinePgo);
            Assert.Equal(1, summary.InlineSingleBlock);
            Assert.Equal(3, summary.InlineNoPgo);
            Assert.Equal(2, summary.BlockCount);
            Assert.Equal(3, summary.InstructionCount);
            Assert.Equal(42, summary.CodeSize);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public void ReadInlineSummaryCountsCompiledMethodsAndInliningOutcomes()
    {
        const string logContent = """
            *************** JIT compiling Namespace.Type:First()
            INLINING SUCCESSFUL: Namespace.Other:Call()
            *************** JIT compiling Namespace.Type:Second()
            INLINING FAILED: Namespace.Other:Fallback()
            INLINING SUCCESSFUL: Namespace.Other:CallAgain()
            """;

        var logPath = CreateTempFile(logContent);

        try
        {
            var summary = JitDumpSummaryReader.ReadInlineSummary(logPath);

            Assert.NotNull(summary);
            Assert.Equal(2, summary!.MethodCount);
            Assert.Equal(2, summary.InlineSuccess);
            Assert.Equal(1, summary.InlineFailed);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content);
        return path;
    }
}
