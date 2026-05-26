using Edict.Benchmarks.Throughput;
using Edict.Substrate.Azurite;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class SubstrateRegistryTests
{
    [Fact]
    public void All_ShouldExposeEveryRegisteredSubstrate()
    {
        var names = SubstrateRegistry.All().Select(s => s.Name).ToArray();
        Assert.Contains("azure", names);
    }

    [Fact]
    public void Resolve_ShouldReturnAzuriteSubstrate_WhenNameIsAzure()
    {
        var substrate = SubstrateRegistry.Resolve("azure");
        Assert.IsType<AzuriteSubstrate>(substrate);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenNameIsUnknown()
    {
        Assert.Null(SubstrateRegistry.Resolve("nonexistent"));
    }
}
