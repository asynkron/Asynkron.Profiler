namespace Asynkron.Profiler;

public class Theme
{
    public string TextColor { get; init; } = "white";
    public string RuntimeTypeColor { get; init; } = "plum1";
    public string CpuValueColor { get; init; } = "green";
    public string CpuCountColor { get; init; } = "blue";
    public string SampleColor { get; init; } = "cyan";
    public string MemoryValueColor { get; init; } = "#c8b6ff";
    public string MemoryCountColor { get; init; } = "#a48def";
    public string AccentColor { get; init; } = "yellow";
    public string ErrorColor { get; init; } = "red";
    public string LeafHighlightColor { get; init; } = "#9a9a9a";
    public string TreeGuideColor { get; init; } = "#7d7d7d";

    public static Theme Current { get; set; } = Default;

    public static Theme Default => new();
    public static Theme OneDark => new()
    {
        TextColor = "#abb2bf",
        RuntimeTypeColor = "#c678dd",
        CpuValueColor = "#98c379",
        CpuCountColor = "#61afef",
        SampleColor = "#56b6c2",
        MemoryValueColor = "#e5c07b",
        MemoryCountColor = "#c678dd",
        AccentColor = "#61afef",
        ErrorColor = "#e06c75",
        LeafHighlightColor = "#5c6370",
        TreeGuideColor = "#5c6370"
    };
    public static Theme Dracula => new()
    {
        TextColor = "#f8f8f2",
        RuntimeTypeColor = "#bd93f9",
        CpuValueColor = "#50fa7b",
        CpuCountColor = "#8be9fd",
        SampleColor = "#ff79c6",
        MemoryValueColor = "#f1fa8c",
        MemoryCountColor = "#bd93f9",
        AccentColor = "#bd93f9",
        ErrorColor = "#ff5555",
        LeafHighlightColor = "#6272a4",
        TreeGuideColor = "#6272a4"
    };
    public static Theme Nord => new()
    {
        TextColor = "#d8dee9",
        RuntimeTypeColor = "#b48ead",
        CpuValueColor = "#a3be8c",
        CpuCountColor = "#88c0d0",
        SampleColor = "#81a1c1",
        MemoryValueColor = "#ebcb8b",
        MemoryCountColor = "#b48ead",
        AccentColor = "#88c0d0",
        ErrorColor = "#bf616a",
        LeafHighlightColor = "#4c566a",
        TreeGuideColor = "#4c566a"
    };
    public static Theme Monokai => new()
    {
        TextColor = "#f8f8f2",
        RuntimeTypeColor = "#ae81ff",
        CpuValueColor = "#a6e22e",
        CpuCountColor = "#66d9ef",
        SampleColor = "#f92672",
        MemoryValueColor = "#e6db74",
        MemoryCountColor = "#ae81ff",
        AccentColor = "#66d9ef",
        ErrorColor = "#f92672",
        LeafHighlightColor = "#75715e",
        TreeGuideColor = "#75715e"
    };

    public static string AvailableThemes => "default, onedark, dracula, nord, monokai";

    public static bool TryResolve(string? name, out Theme theme)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            theme = Current;
            return true;
        }

        var normalized = name.Trim().ToLowerInvariant();
        Theme? resolved = normalized switch
        {
            "default" => Default,
            "onedark" or "one-dark" or "one_dark" => OneDark,
            "dracula" => Dracula,
            "nord" => Nord,
            "monokai" => Monokai,
            _ => null
        };

        if (resolved == null)
        {
            theme = Current;
            return false;
        }

        theme = resolved;
        return true;
    }
}
