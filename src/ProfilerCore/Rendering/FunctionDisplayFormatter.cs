using Spectre.Console;

namespace Asynkron.Profiler;

internal static class FunctionDisplayFormatter
{
    public static string FormatFunctionCell(string rawName, string runtimeTypeColor, int maxLength = 70)
    {
        var funcName = CallTreeHelpers.FormatFunctionDisplayName(rawName);
        if (funcName.Length > maxLength)
        {
            funcName = funcName[..(maxLength - 3)] + "...";
        }

        var funcText = Markup.Escape(funcName);
        if (CallTreeHelpers.IsUnmanagedFrame(funcName))
        {
            funcText = $"[{runtimeTypeColor}]{funcText}[/]";
        }

        return funcText;
    }
}
