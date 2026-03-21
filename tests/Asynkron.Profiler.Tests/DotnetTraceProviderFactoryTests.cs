using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class DotnetTraceProviderFactoryTests
{
    [Fact]
    public void BuildCpuProviderList_UsesSampleProfilerByDefault()
    {
        var providerList = DotnetTraceProviderFactory.BuildCpuProviderList(includeException: false);

        Assert.Equal("Microsoft-DotNETCore-SampleProfiler", providerList);
    }

    [Fact]
    public void BuildCpuProviderList_AppendsExceptionProviderWhenRequested()
    {
        var providerList = DotnetTraceProviderFactory.BuildCpuProviderList(includeException: true);

        Assert.Contains("Microsoft-DotNETCore-SampleProfiler", providerList);
        Assert.Contains(DotnetTraceProviderFactory.BuildExceptionProvider(), providerList);
    }

    [Fact]
    public void BuildContentionProvider_UsesExpectedEtwProviderFormat()
    {
        var provider = DotnetTraceProviderFactory.BuildContentionProvider();

        Assert.StartsWith("Microsoft-Windows-DotNETRuntime:0x", provider);
        Assert.EndsWith(":4", provider);
    }
}
