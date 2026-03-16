using System;

namespace Asynkron.Profiler;

internal readonly record struct CallTreeTraversalSettings(
    int MaxDepth,
    int MaxWidth,
    int SiblingCutoffPercent)
{
    public static CallTreeTraversalSettings Create(int maxDepth, int maxWidth, int siblingCutoffPercent)
    {
        return new CallTreeTraversalSettings(
            Math.Max(1, maxDepth),
            Math.Max(1, maxWidth),
            Math.Max(0, siblingCutoffPercent));
    }
}
