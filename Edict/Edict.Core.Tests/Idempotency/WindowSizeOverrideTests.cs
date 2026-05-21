namespace Edict.Core.Tests.Idempotency;

// ADR 0028: EdictIdempotencyBase.WindowSize reads its silo-wide default from
// EdictOptions.IdempotencyWindowSize, while a per-grain-type override still
// wins — so a singleton high-throughput consumer can choose its own window
// without changing the operator-picked default for everyone else.
public sealed class WindowSizeOverrideTests(WindowSizeClusterFixture fixture)
    : IClassFixture<WindowSizeClusterFixture>
{
    [Fact]
    public async Task WindowSize_ShouldComeFromConfiguredOptions_WhenSubclassDoesNotOverride()
    {
        var probe = fixture.Cluster.GrainFactory.GetGrain<IWindowSizeDefaultProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.Equal(WindowSizeClusterFixture.ConfiguredWindowSize, effectiveWindow);
    }

    [Fact]
    public async Task WindowSize_ShouldHonourPerGrainOverride_OverConfiguredOptions()
    {
        var probe = fixture.Cluster.GrainFactory.GetGrain<IWindowSizeOverrideProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.NotEqual(WindowSizeClusterFixture.ConfiguredWindowSize, effectiveWindow);
        Assert.Equal(7, effectiveWindow);
    }
}
