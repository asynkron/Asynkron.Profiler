using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed record ExceptionTypeDetails(
    long Thrown,
    CallTreeNode ThrowRoot,
    long Caught,
    CallTreeNode? CatchRoot,
    IReadOnlyList<ExceptionSiteSample> CatchSites);
