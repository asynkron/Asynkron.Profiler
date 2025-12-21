using System.Collections.Generic;

namespace Asynkron.Profiler;

internal sealed class CallTreeNode
{
    public CallTreeNode(int frameIdx, string name)
    {
        FrameIdx = frameIdx;
        Name = name;
    }

    public int FrameIdx { get; }
    public string Name { get; }
    public double Total { get; set; }
    public double Self { get; set; }
    public int Calls { get; set; }
    public Dictionary<int, CallTreeNode> Children { get; } = new();
}
