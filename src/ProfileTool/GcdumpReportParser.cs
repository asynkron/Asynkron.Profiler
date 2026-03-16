using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
                if (line.Contains("Size", StringComparison.Ordinal) &&
                    line.Contains("Count", StringComparison.Ordinal) &&
                    line.Contains("Type", StringComparison.Ordinal))
                {
                    inTable = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            if (!TryParseLong(parts[0], out var size) || !TryParseLong(parts[1], out var count))
            {
                continue;
            }

            var typeName = string.Join(' ', parts.Skip(2));
            types.Add(new HeapTypeEntry(size, count, typeName));
        }

        return new HeapProfileResult(output, types);
    }

    private static bool TryParseLong(string input, out long value)
    {
        return long.TryParse(
            input.Replace(",", string.Empty, StringComparison.Ordinal),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }
}
