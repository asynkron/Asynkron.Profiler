using System;
using Spectre.Console;
using Spectre.Console.Rendering;
using static Asynkron.Profiler.CallTreeHelpers;

namespace Asynkron.Profiler;

internal sealed class ProfilerCallTreeRenderer
{
    private const double DefaultHotThreshold = 0.4d;

    private readonly Theme _theme;
    private readonly ProfilerCallTreeFormatter _formatter;
    private readonly ProfilerTreeFactory _treeFactory;
    private readonly ProfilerCallTreeRootResolver _rootResolver;
    private readonly ProfilerCallTreeNodeDecorator _nodeDecorator;
    private readonly ProfilerExceptionTreeRenderer _exceptionRenderer;
    private readonly ProfilerAllocationTreeRenderer _allocationRenderer;
    private readonly ProfilerTimelineRowsRenderer _timelineRenderer;

    public ProfilerCallTreeRenderer(Theme theme, ProfilerCallTreeFormatter formatter)
    {
        _theme = theme;
        _formatter = formatter;
        _treeFactory = new ProfilerTreeFactory(theme);
        _rootResolver = new ProfilerCallTreeRootResolver(theme);
        _nodeDecorator = new ProfilerCallTreeNodeDecorator(theme);
        _exceptionRenderer = new ProfilerExceptionTreeRenderer(theme, formatter, _treeFactory);
        _allocationRenderer = new ProfilerAllocationTreeRenderer(formatter, _treeFactory);
        _timelineRenderer = new ProfilerTimelineRowsRenderer(theme, formatter);
    }

    public Rows BuildCpuCallTree(
        CpuProfileResult results,
        bool useSelfTime,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool showTimeline = false,
        int timelineWidth = 40)
    {
        var callTreeRoot = results.CallTreeRoot;
        var totalTime = results.CallTreeTotal;
        var totalSamples = callTreeRoot.Calls;
        var traversal = CallTreeTraversalSettings.Create(maxDepth, maxWidth, siblingCutoffPercent);
        var (rootSelection, context) = CreateRenderContext(
            callTreeRoot,
            totalTime,
            totalSamples,
            useSelfTime ? "Call Tree (Self Time)" : "Call Tree (Total Time)",
            rootFilter,
            includeRuntime,
            rootMode,
            useSelfTime,
            traversal,
            timeUnitLabel,
            countSuffix,
            allocationTypeLimit,
            exceptionTypeLimit,
            hotThreshold,
            highlightHotspots: true);

        if (showTimeline && rootSelection.RootNode.HasTiming)
        {
            return _timelineRenderer.Build(rootSelection.RootNode, rootSelection.Title, context, timelineWidth);
        }

        return BuildStandardTreeRows(rootSelection.RootNode, rootSelection.Title, context);
    }

    public Rows BuildContentionCallTree(
        ContentionProfileResult results,
        string? rootFilter,
        bool includeRuntime,
        int maxDepth,
        int maxWidth,
        string? rootMode,
        int siblingCutoffPercent)
    {
        var callTreeRoot = results.CallTreeRoot;
        var totalTime = results.TotalWaitMs;
        var totalSamples = callTreeRoot.Calls;
        var traversal = CallTreeTraversalSettings.Create(maxDepth, maxWidth, siblingCutoffPercent);
        var (rootSelection, context) = CreateRenderContext(
            callTreeRoot,
            totalTime,
            totalSamples,
            "Call Tree (Wait Time)",
            rootFilter,
            includeRuntime,
            rootMode,
            useSelfTime: false,
            traversal,
            "ms",
            "x",
            allocationTypeLimit: 0,
            exceptionTypeLimit: 0,
            hotThreshold: DefaultHotThreshold,
            highlightHotspots: false);

        return BuildStandardTreeRows(rootSelection.RootNode, rootSelection.Title, context);
    }

    public Rows BuildExceptionCallTree(ProfilerExceptionCallTreeRequest request)
    {
        return _exceptionRenderer.Build(request);
    }

    public void PrintAllocationCallTree(ProfilerAllocationCallTreeRequest request)
    {
        AnsiConsole.Write(_allocationRenderer.Build(request));
    }

    public string HighlightJitNumbers(string text) => _formatter.HighlightJitNumbers(text);

    private (ProfilerCallTreeRootSelection RootSelection, ProfilerCallTreeRenderContext Context) CreateRenderContext(
        CallTreeNode callTreeRoot,
        double totalTime,
        int totalSamples,
        string title,
        string? rootFilter,
        bool includeRuntime,
        string? rootMode,
        bool useSelfTime,
        CallTreeTraversalSettings traversal,
        string timeUnitLabel,
        string countSuffix,
        int allocationTypeLimit,
        int exceptionTypeLimit,
        double hotThreshold,
        bool highlightHotspots)
    {
        var rootSelection = _rootResolver.Resolve(
            callTreeRoot,
            totalTime,
            title,
            rootFilter,
            includeRuntime,
            rootMode);

        var context = new ProfilerCallTreeRenderContext(
            rootSelection.RootTotal,
            totalSamples,
            useSelfTime,
            includeRuntime,
            traversal,
            timeUnitLabel,
            countSuffix,
            allocationTypeLimit,
            exceptionTypeLimit,
            hotThreshold,
            highlightHotspots);

        return (rootSelection, context);
    }

    private Rows BuildStandardTreeRows(
        CallTreeNode rootNode,
        string title,
        ProfilerCallTreeRenderContext context)
    {
        var rootLabel = _formatter.FormatCallTreeLine(
            rootNode,
            context.RootTotal,
            context.TotalSamples,
            context.UseSelfTime,
            isRoot: true,
            context.TimeUnitLabel,
            context.CountSuffix,
            isLeaf: false,
            timeline: null,
            depth: 0,
            useHeatColor: true);
        var tree = _treeFactory.Create(rootLabel);

        _nodeDecorator.Decorate(tree, rootNode, context);
        AddCallTreeChildren(
            tree,
            rootNode,
            context,
            depth: 1,
            timeline: null);

        return new Rows(
            new Markup($"[bold {_theme.AccentColor}]{title}[/]"),
            tree);
    }

    private void AddCallTreeChildren(
        IHasTreeNodes parent,
        CallTreeNode node,
        ProfilerCallTreeRenderContext context,
        int depth,
        TimelineContext? timeline = null)
    {
        if (depth > context.Traversal.MaxDepth)
        {
            return;
        }

        var children = CallTreeFilters.GetVisibleChildren(
            node,
            context.IncludeRuntime,
            context.UseSelfTime,
            context.Traversal.MaxWidth,
            context.Traversal.SiblingCutoffPercent,
            CallTreeHelpers.IsRuntimeNoise);

        foreach (var child in children)
        {
            var childHotness = ComputeHotness(child, context.RootTotal, context.TotalSamples);
            var isHotspot = context.HighlightHotspots && ProfilerHotnessColorScale.IsHotspot(childHotness, context.HotThreshold);
            var (childNode, isSpecialLeaf) = AddCallTreeChildNode(
                parent,
                child,
                context,
                depth,
                timeline,
                isHotspot);
            AddCallTreeDecorationsAndChildren(
                childNode,
                child,
                context,
                isSpecialLeaf,
                () => AddCallTreeChildren(
                    childNode,
                    child,
                    context,
                    depth + 1,
                    timeline));
        }
    }

    private (TreeNode Node, bool IsSpecialLeaf) AddCallTreeChildNode(
        IHasTreeNodes parent,
        CallTreeNode child,
        ProfilerCallTreeRenderContext context,
        int depth,
        TimelineContext? timeline = null,
        bool isHotspot = false,
        bool useHeatColor = false)
    {
        var isSpecialLeaf = ShouldStopAtLeaf(GetCallTreeMatchName(child));
        var isLeaf = isSpecialLeaf || depth >= context.Traversal.MaxDepth ||
                     !CallTreeFilters.HasVisibleChildren(
                         child,
                         context.IncludeRuntime,
                         context.UseSelfTime,
                         context.Traversal.MaxWidth,
                         context.Traversal.SiblingCutoffPercent,
                         CallTreeHelpers.IsRuntimeNoise);

        var childNode = parent.AddNode(_formatter.FormatCallTreeLine(
            child,
            context.RootTotal,
            context.TotalSamples,
            context.UseSelfTime,
            isRoot: false,
            context.TimeUnitLabel,
            context.CountSuffix,
            isLeaf,
            timeline,
            depth,
            isHotspot,
            useHeatColor));

        return (childNode, isSpecialLeaf);
    }

    private void AddCallTreeDecorationsAndChildren(
        TreeNode childNode,
        CallTreeNode child,
        ProfilerCallTreeRenderContext context,
        bool isSpecialLeaf,
        Action addChildren)
    {
        _nodeDecorator.Decorate(childNode, child, context);

        if (!isSpecialLeaf)
        {
            addChildren();
        }
    }
}
