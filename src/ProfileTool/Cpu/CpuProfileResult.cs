using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed record CpuProfileResult(
    IReadOnlyList<FunctionSample> AllFunctions,
    double TotalTime,
    CallTreeNode CallTreeRoot,
    double CallTreeTotal,
    string? SpeedscopePath,
    string TimeUnitLabel,
    string CountLabel,
    string CountSuffix)
{
    public static CpuProfileResult CreateTraceResult(
        IReadOnlyList<FunctionSample> allFunctions,
        double totalTime,
        CallTreeNode callTreeRoot,
        double callTreeTotal,
        string? sourcePath)
    {
        return new CpuProfileResult(
            allFunctions,
            totalTime,
            callTreeRoot,
            callTreeTotal,
            sourcePath,
            "ms",
            "Samples",
            " samp");
    }
}
