using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed record CpuProfileResult(
    IReadOnlyList<FunctionSample> AllFunctions,
    double TotalTime,
    CallTreeNode CallTreeRoot,
    double CallTreeTotal,
    string? SpeedscopePath,
    string TimeUnitLabel,
    string CountLabel,
    string CountSuffix);
