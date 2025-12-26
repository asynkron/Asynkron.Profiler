namespace Asynkron.Profiler;

/// <summary>
/// Context for timeline rendering with call tree.
/// </summary>
public sealed class TimelineContext
{
    public double RootStart { get; init; }
    public double RootEnd { get; init; }
    public int BarWidth { get; init; }
    public int TextWidth { get; init; }
    public int MaxNameLength { get; init; }
    public int MaxDepth { get; init; }

    public double RootDuration => RootEnd - RootStart;

    /// <summary>
    /// Calculate padding needed to align timeline separator at a fixed X position.
    /// The tree guides add ~4 chars per level (branches can add more), so deeper nodes need less padding.
    /// </summary>
    public int GetPaddingForDepth(int depth, int visibleTextLength)
    {
        // Tree guides add ~4 chars per level (can be more with branches)
        var treeGuideWidth = depth * 4;
        // Target X position is TextWidth (which is the timeline X position)
        // Content so far = tree guides + visible text
        var contentWidth = treeGuideWidth + visibleTextLength;
        return Math.Max(0, TextWidth - contentWidth);
    }
}
