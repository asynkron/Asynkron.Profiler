using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed class ContentionProfileResult
{
    public ContentionProfileResult(
        IReadOnlyList<FunctionSample> topFunctions,
        CallTreeNode callTreeRoot,
        double totalWaitMs,
        long totalCount)
    {
        TopFunctions = topFunctions;
        CallTreeRoot = callTreeRoot;
        TotalWaitMs = totalWaitMs;
        TotalCount = totalCount;
    }

    public IReadOnlyList<FunctionSample> TopFunctions { get; }
    public CallTreeNode CallTreeRoot { get; }
    public double TotalWaitMs { get; }
    public long TotalCount { get; }
}
