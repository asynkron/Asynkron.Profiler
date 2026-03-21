using System;

namespace Asynkron.Profiler;

public static class NameFormatter
{
    public static string FormatMethodDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        if (IsUnmanagedCode(rawName))
        {
            return "Unmanaged Code";
        }

        var name = StripParameterList(rawName);
        if (IsUnmanagedCode(name))
        {
            return "Unmanaged Code";
        }

        if (name.Contains('!'))
        {
            name = StripParameterList(name.Split('!')[^1]);
        }

        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0 && lastDot < name.Length - 1)
        {
            var typePart = name[..lastDot].TrimEnd('.');
            var methodPart = name[(lastDot + 1)..];
            var compilerGenerated = CompilerGeneratedMethodDisplayFormatter.TryFormat(typePart, methodPart);
            if (!string.IsNullOrWhiteSpace(compilerGenerated))
            {
                return compilerGenerated;
            }

            var formatted = $"{TypeDisplayNameFormatter.Format(typePart)}.{methodPart}";
            return EnsureReadableName(formatted);
        }

        return EnsureReadableName(TypeDisplayNameFormatter.Format(name));
    }

    public static string FormatTypeDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        return TypeDisplayNameFormatter.Format(rawName);
    }

    private static bool IsUnmanagedCode(string name)
    {
        return FrameNameClassifier.IsExplicitUnmanagedCode(name);
    }

    private static string StripParameterList(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var trimmed = name.Trim();
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx >= 0)
        {
            trimmed = trimmed[..parenIdx];
        }
        else
        {
            var cutIdx = FindFirstTopLevelSeparator(trimmed);
            if (cutIdx >= 0)
            {
                trimmed = trimmed[..cutIdx];
            }
        }

        return trimmed.TrimEnd(')', '&', ',', ' ');
    }

    private static string EnsureReadableName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (!FrameNameClassifier.ContainsLetter(trimmed))
        {
            return "Unmanaged Code";
        }

        return trimmed;
    }

    private static int FindFirstTopLevelSeparator(string name)
    {
        var squareDepth = 0;
        var angleDepth = 0;

        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            switch (ch)
            {
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    if (squareDepth > 0)
                    {
                        squareDepth--;
                    }
                    break;
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    if (angleDepth > 0)
                    {
                        angleDepth--;
                    }
                    break;
                case ',' when squareDepth == 0 && angleDepth == 0:
                case ')' when squareDepth == 0 && angleDepth == 0:
                    return i;
            }
        }

        return -1;
    }
}
