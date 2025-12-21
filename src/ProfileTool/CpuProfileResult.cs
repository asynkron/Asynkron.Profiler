using System.Collections.Generic;

namespace Asynkron.JsEngine.Tools.ProfileTool;

internal sealed record CpuProfileResult(
    IReadOnlyList<FunctionSample> AllFunctions,
    double TotalTime,
    CallTreeNode CallTreeRoot,
    double CallTreeTotal);
