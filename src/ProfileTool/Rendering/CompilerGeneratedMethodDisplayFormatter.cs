using System;

namespace Asynkron.Profiler;

internal static class CompilerGeneratedMethodDisplayFormatter
{
    private static readonly char[] MethodTerminatorCharacters = new[] { '|', '>' };

    public static string? TryFormat(string typePart, string methodPart)
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
        if (string.IsNullOrWhiteSpace(lambdaOwner))
        {
            return null;
        }

        var ownerType = IsDisplayClassType(typePart)
            ? ExtractOuterType(typePart)
            : typePart;
        var prefix = string.IsNullOrWhiteSpace(ownerType)
            ? string.Empty
            : TypeDisplayNameFormatter.Format(ownerType) + ".";

        return $"{prefix}{lambdaOwner} lambda";
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
            var localEnd = typePart.IndexOfAny(MethodTerminatorCharacters, localStart);
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
        while (trimmed.StartsWith('<') || trimmed.EndsWith('>'))
        {
            trimmed = trimmed.Trim('<', '>');
        }

        while (trimmed.EndsWith('$'))
        {
            trimmed = trimmed[..^1];
            while (trimmed.EndsWith('>'))
            {
                trimmed = trimmed.TrimEnd('>');
            }
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
