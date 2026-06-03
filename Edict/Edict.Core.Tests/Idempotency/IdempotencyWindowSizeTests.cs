using Edict.Core.Tests.TestSupport;

using Xunit;

namespace Edict.Core.Tests.Idempotency;

// EdictIdempotencyBase.WindowSize reads its silo-wide default from
// EdictOptions.IdempotencyWindowSize, while a per-grain override still wins — a
// high-throughput singleton consumer can pick its own window without changing
// the operator-picked default for everyone else. Substrate-independent — proven
// once in-memory.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class IdempotencyWindowSizeTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public IdempotencyWindowSizeTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WindowSize_ShouldComeFromConfiguredOptions_WhenSubclassDoesNotOverride()
    {
        var probe = _fixture.GrainFactory.GetGrain<IWindowSizeDefaultProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.Equal(SubstrateIndependentClusterFixture.ConfiguredWindowSize, effectiveWindow);
    }

    [Fact]
    public async Task WindowSize_ShouldHonourPerGrainOverride_OverConfiguredOptions()
    {
        var probe = _fixture.GrainFactory.GetGrain<IWindowSizeOverrideProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.NotEqual(SubstrateIndependentClusterFixture.ConfiguredWindowSize, effectiveWindow);
        Assert.Equal(7, effectiveWindow);
    }
}
