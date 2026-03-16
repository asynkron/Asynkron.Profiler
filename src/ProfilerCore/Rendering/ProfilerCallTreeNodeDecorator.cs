using System.Globalization;
using System.Linq;
using Spectre.Console;

namespace Asynkron.Profiler;

internal sealed class ProfilerCallTreeNodeDecorator
{
    private readonly Theme _theme;

    public ProfilerCallTreeNodeDecorator(Theme theme)
    {
        _theme = theme;
    }

    public void Decorate(IHasTreeNodes parent, CallTreeNode node, ProfilerCallTreeRenderContext context)
    {
        AddAllocationTypeNodes(parent, node, context.AllocationTypeLimit);
        AddExceptionTypeNodes(parent, node, context.ExceptionTypeLimit);
    }

    private void AddAllocationTypeNodes(IHasTreeNodes parent, CallTreeNode node, int limit)
    {
        if (limit <= 0 || node.AllocationByType == null || node.AllocationByType.Count == 0)
        {
            return;
        }

        foreach (var entry in node.AllocationByType.OrderByDescending(kv => kv.Value).Take(limit))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Key);
            var bytesText = CallTreeHelpers.FormatBytes(entry.Value);
            var count = node.AllocationCountByType != null &&
                        node.AllocationCountByType.TryGetValue(entry.Key, out var allocationCount)
                ? allocationCount
                : 0;
            var countText = count > 0
                ? count.ToString("N0", CultureInfo.InvariantCulture) + "x"
                : "0x";
            parent.AddNode($"[{_theme.MemoryValueColor}]{bytesText}[/] [{_theme.MemoryCountColor}]{countText}[/] {Markup.Escape(typeName)}");
        }
    }

    private void AddExceptionTypeNodes(IHasTreeNodes parent, CallTreeNode node, int limit)
    {
        if (limit <= 0 || node.ExceptionByType == null || node.ExceptionByType.Count == 0)
        {
            return;
        }

        foreach (var entry in node.ExceptionByType.OrderByDescending(kv => kv.Value).Take(limit))
        {
            var typeName = NameFormatter.FormatTypeDisplayName(entry.Key);
            var countText = entry.Value.ToString("N0", CultureInfo.InvariantCulture) + "x";
            parent.AddNode($"[{_theme.ErrorColor}]{countText}[/] {Markup.Escape(typeName)}");
        }
    }
}
