using System;
using System.Collections.Generic;
using System.Linq;

namespace Asynkron.Profiler;

internal static class ExceptionSiteSampleBuilder
{
    public static IReadOnlyList<ExceptionSiteSample> Create(IReadOnlyDictionary<string, long>? sites)
    {
        return sites == null
            ? Array.Empty<ExceptionSiteSample>()
            : sites.OrderByDescending(kv => kv.Value)
                .Select(kv => new ExceptionSiteSample(kv.Key, kv.Value))
                .ToList();
    }
}
