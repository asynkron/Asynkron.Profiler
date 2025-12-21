using System;
using System.Text.RegularExpressions;

namespace Asynkron.Profiler;

internal static class NameFormatter
{
    public static string FormatMethodDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return rawName;
        }

        var name = rawName;
        if (name.Contains('!'))
        {
            name = name.Split('!')[^1];
        }

        var parenIdx = name.IndexOf('(');
        if (parenIdx > 0)
        {
            name = name[..parenIdx];
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

            return $"{CleanTypeName(typePart)}.{methodPart}";
        }

        return CleanTypeName(name);
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
        normalized = normalized.Replace("[]", arrayToken, StringComparison.Ordinal);
        normalized = normalized.Replace('[', '<').Replace(']', '>');
        normalized = normalized.Replace(arrayToken, "[]", StringComparison.Ordinal);
        normalized = normalized.Replace('+', '.');

        return normalized;
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

        var owner = methodPart[(ownerStart + 1)..ownerEnd];
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
