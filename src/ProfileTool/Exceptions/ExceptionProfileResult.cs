using System.Collections.Generic;

namespace Asynkron.Profiler;

public sealed record ExceptionProfileResult(
    IReadOnlyList<ExceptionTypeSample> ExceptionTypes,
    CallTreeNode ThrowCallTreeRoot,
    long TotalThrown,
    IReadOnlyDictionary<string, ExceptionTypeDetails> TypeDetails,
    IReadOnlyList<ExceptionSiteSample> CatchSites,
    CallTreeNode? CatchCallTreeRoot,
    long TotalCaught);
