using System.Collections.Generic;
using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class ProfilerRenderFiltersTests
{
    [Fact]
    public void NormalizeCallTreeRootFilter_ReturnsNullForWhitespace()
    {
        Assert.Null(ProfilerRenderFilters.NormalizeCallTreeRootFilter(" \t "));
    }

    [Fact]
    public void MatchesFunctionFilter_MatchesFormattedDisplayName()
    {
        var matches = ProfilerRenderFilters.MatchesFunctionFilter(
            "System.Collections.Generic.List`1.Add(System.String)",
            "List.Add");

        Assert.True(matches);
    }

    [Fact]
    public void FilterExceptionTypes_MatchesFormattedTypeName()
    {
        IReadOnlyList<ExceptionTypeSample> types =
        [
            new("System.InvalidOperationException", 2),
            new("MyCompany.Errors.CustomProblem", 1)
        ];

        var filtered = ProfilerRenderFilters.FilterExceptionTypes(types, "InvalidOperationException");

        var entry = Assert.Single(filtered);
        Assert.Equal("System.InvalidOperationException", entry.Type);
    }

    [Fact]
    public void SelectExceptionType_ReturnsFirstFormattedMatch()
    {
        IReadOnlyList<ExceptionTypeSample> types =
        [
            new("System.Collections.Generic.KeyNotFoundException", 2),
            new("System.InvalidOperationException", 1)
        ];

        var selected = ProfilerRenderFilters.SelectExceptionType(types, "KeyNotFoundException");

        Assert.Equal("System.Collections.Generic.KeyNotFoundException", selected);
    }
}
