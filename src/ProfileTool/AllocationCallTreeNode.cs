using System;
using System.Collections.Generic;

namespace Asynkron.JsEngine.Tools.ProfileTool;

internal sealed class AllocationCallTreeNode
{
    public AllocationCallTreeNode(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public long TotalBytes { get; set; }
    public long Count { get; set; }
    public Dictionary<string, AllocationCallTreeNode> Children { get; } =
        new(StringComparer.Ordinal);
}
