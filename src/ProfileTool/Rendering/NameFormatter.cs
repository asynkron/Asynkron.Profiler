using System;
using System.Text.RegularExpressions;

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
            var compilerGenerated = FormatCompilerGeneratedMethod(typePart, methodPart);
            if (!string.IsNullOrWhiteSpace(compilerGenerated))
            {
                return compilerGenerated;
            }

            var formatted = $"{CleanTypeName(typePart)}.{methodPart}";
            return EnsureReadableName(formatted);
        }

        return EnsureReadableName(CleanTypeName(name));
    }

    public static string FormatTypeDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        return CleanTypeName(rawName);
    }

    private static string CleanTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var timeout = TimeSpan.FromMilliseconds(100);
        var normalized = Regex.Replace(
            name,
            @"\b(?:[A-Za-z_][A-Za-z0-9_]*\.)+(?<type>[A-Za-z_][A-Za-z0-9_]*)",
            "${type}",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);

        normalized = Regex.Replace(
            normalized,
            @"`\d+",
            "",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);

        const string arrayToken = "__ARRAY__";
        normalized = Regex.Replace(
            normalized,
            @"\[(?<commas>,*)\]",
            match => $"{arrayToken}{match.Groups["commas"].Value}{arrayToken}",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);
        normalized = normalized.Replace('[', '<').Replace(']', '>');
        normalized = Regex.Replace(
            normalized,
            $"{arrayToken}(?<commas>,*){arrayToken}",
            match => $"[{match.Groups["commas"].Value}]",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);
        normalized = normalized.Replace('+', '.');

        return normalized;
    }

    private static bool IsUnmanagedCode(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        return trimmed.Contains("UNMANAGED_CODE_TIME", StringComparison.OrdinalIgnoreCase);
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
        if (!HasLetter(trimmed))
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

    private static bool HasLetter(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsLetter(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FormatCompilerGeneratedMethod(string typePart, string methodPart)
    {
        if (string.IsNullOrWhiteSpace(typePart) || string.IsNullOrWhiteSpace(methodPart))
        {
            return null;
        }

        if (string.Equals(methodPart, "MoveNext", StringComparison.Ordinal))
        {
            var stateMethod = ExtractStateMachineMethodName(typePart);
            if (!string.IsNullOrWhiteSpace(stateMethod))
            {
                return $"StateMachine.{stateMethod}.MoveNext";
            }
        }

        var lambdaOwner = ExtractLambdaOwner(methodPart);
        if (!string.IsNullOrWhiteSpace(lambdaOwner) && IsDisplayClassType(typePart))
        {
            var outerType = ExtractOuterType(typePart);
            var prefix = string.IsNullOrWhiteSpace(outerType)
                ? string.Empty
                : CleanTypeName(outerType) + ".";
            return $"{prefix}{lambdaOwner} lambda";
        }

        if (!string.IsNullOrWhiteSpace(lambdaOwner))
        {
            var prefix = string.IsNullOrWhiteSpace(typePart)
                ? string.Empty
                : CleanTypeName(typePart) + ".";
            return $"{prefix}{lambdaOwner} lambda";
        }

        return null;
    }

    private static bool IsDisplayClassType(string typePart)
    {
        return typePart.Contains("<>c__DisplayClass", StringComparison.Ordinal) ||
               typePart.Contains("+<>c", StringComparison.Ordinal);
    }

    private static string? ExtractStateMachineMethodName(string typePart)
    {
        var localFunctionIndex = typePart.LastIndexOf("g__", StringComparison.Ordinal);
        if (localFunctionIndex >= 0)
        {
            var localStart = localFunctionIndex + 3;
            var localEnd = typePart.IndexOfAny(new[] { '|', '>' }, localStart);
            if (localEnd < 0)
            {
                localEnd = typePart.Length;
            }

            var name = typePart[localStart..localEnd];
            return TrimCompilerGeneratedName(name);
        }

        var methodEnd = typePart.LastIndexOf(">d__", StringComparison.Ordinal);
        if (methodEnd < 0)
        {
            methodEnd = typePart.LastIndexOf(">d", StringComparison.Ordinal);
        }

        if (methodEnd < 0)
        {
            methodEnd = typePart.LastIndexOf('>');
        }

        if (methodEnd < 0)
        {
            return null;
        }

        var methodStart = typePart.LastIndexOf('<', methodEnd);
        if (methodStart < 0 || methodStart + 1 >= methodEnd)
        {
            return null;
        }

        var methodName = typePart[(methodStart + 1)..methodEnd];
        return TrimCompilerGeneratedName(methodName);
    }

    private static string? ExtractLambdaOwner(string methodPart)
    {
        if (string.IsNullOrWhiteSpace(methodPart))
        {
            return null;
        }

        var ownerStart = methodPart.IndexOf('<');
        var ownerEnd = methodPart.IndexOf('>');
        if (ownerStart < 0 || ownerEnd <= ownerStart)
        {
            return null;
        }

        var owner = TrimCompilerGeneratedName(methodPart[(ownerStart + 1)..ownerEnd]);
        return string.IsNullOrWhiteSpace(owner) ? null : owner;
    }

    private static string ExtractOuterType(string typePart)
    {
        var markerIndex = typePart.IndexOf("+<", StringComparison.Ordinal);
        if (markerIndex > 0)
        {
            return typePart[..markerIndex];
        }

        markerIndex = typePart.IndexOf("+<>c", StringComparison.Ordinal);
        if (markerIndex > 0)
        {
            return typePart[..markerIndex];
        }

        return typePart;
    }

    private static string? TrimCompilerGeneratedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        while (trimmed.StartsWith("<", StringComparison.Ordinal) ||
               trimmed.EndsWith(">", StringComparison.Ordinal))
        {
            trimmed = trimmed.Trim('<', '>');
        }

        while (trimmed.EndsWith("$", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
            while (trimmed.EndsWith(">", StringComparison.Ordinal))
            {
                trimmed = trimmed.TrimEnd('>');
            }
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
