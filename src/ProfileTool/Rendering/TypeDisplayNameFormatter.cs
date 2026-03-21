using System;
using System.Text.RegularExpressions;

namespace Asynkron.Profiler;

internal static class TypeDisplayNameFormatter
{
    private const string ArrayToken = "__ARRAY__";

    public static string Format(string name)
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
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);

        normalized = Regex.Replace(
            normalized,
            @"\[(?<commas>,*)\]",
            match => $"{ArrayToken}{match.Groups["commas"].Value}{ArrayToken}",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);
        normalized = normalized.Replace('[', '<').Replace(']', '>');
        normalized = Regex.Replace(
            normalized,
            $"{ArrayToken}(?<commas>,*){ArrayToken}",
            match => $"[{match.Groups["commas"].Value}]",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
            timeout);

        return normalized.Replace('+', '.');
    }
}
