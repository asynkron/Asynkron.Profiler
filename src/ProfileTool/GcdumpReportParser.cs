using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Asynkron.Profiler;

internal static class GcdumpReportParser
{
    public static HeapProfileResult Parse(string output)
    {
        var types = new List<HeapTypeEntry>();
        using var reader = new StringReader(output);
        var inTable = false;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!inTable)
            {
                if (line.Contains("Size", System.StringComparison.Ordinal) &&
                    line.Contains("Count", System.StringComparison.Ordinal) &&
                    line.Contains("Type", System.StringComparison.Ordinal))
                {
                    inTable = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            if (!TryParseLong(parts[0], out var size) || !TryParseLong(parts[1], out var count))
            {
                continue;
            }

            var typeName = string.Join(' ', parts.AsSpan(2).ToArray());
            types.Add(new HeapTypeEntry(size, count, typeName));
        }

        return new HeapProfileResult(output, types);
    }

    private static bool TryParseLong(string input, out long value)
    {
        return long.TryParse(
            input.Replace(",", string.Empty, System.StringComparison.Ordinal),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }
}
