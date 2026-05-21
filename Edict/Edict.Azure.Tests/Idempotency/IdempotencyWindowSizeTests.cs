namespace Edict.Azure.Tests.Idempotency;

/// <summary>
/// ADR 0028: <c>EdictIdempotencyBase.WindowSize</c> reads its silo-wide
/// default from <c>EdictOptions.IdempotencyWindowSize</c>, while a per-grain
/// override still wins — a high-throughput singleton consumer can pick its
/// own window without changing the operator-picked default for everyone else.
/// </summary>
[Collection(IdempotencyWindowSizeClusterCollection.Name)]
public sealed class IdempotencyWindowSizeTests(IdempotencyWindowSizeClusterFixture fixture)
{
    [Fact]
    public async Task WindowSize_ShouldComeFromConfiguredOptions_WhenSubclassDoesNotOverride()
    {
        var probe = fixture.Cluster.GrainFactory
            .GetGrain<IIdempotencyWindowSizeDefaultProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.Equal(IdempotencyWindowSizeClusterFixture.ConfiguredWindowSize, effectiveWindow);
    }

    [Fact]
    public async Task WindowSize_ShouldHonourPerGrainOverride_OverConfiguredOptions()
    {
        var probe = fixture.Cluster.GrainFactory
            .GetGrain<IIdempotencyWindowSizeOverrideProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.NotEqual(IdempotencyWindowSizeClusterFixture.ConfiguredWindowSize, effectiveWindow);
        Assert.Equal(7, effectiveWindow);
    }
}
