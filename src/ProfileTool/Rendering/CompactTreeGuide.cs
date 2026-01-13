using Spectre.Console;
using Spectre.Console.Rendering;

namespace Asynkron.Profiler;

public sealed class CompactTreeGuide : TreeGuide
{
    public override string GetPart(TreeGuidePart part)
    {
        return part switch
        {
            TreeGuidePart.Space => "   ",
            TreeGuidePart.Continue => "│  ",
            TreeGuidePart.Fork => "├─ ",
            TreeGuidePart.End => "└─ ",
            _ => "   "
        };
    }
}
