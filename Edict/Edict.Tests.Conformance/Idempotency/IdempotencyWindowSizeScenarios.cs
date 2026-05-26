using Xunit;

namespace Edict.Tests.Conformance.Idempotency;

/// <summary>
/// <see cref="Core.Idempotency.EdictIdempotencyBase.WindowSize"/> reads its
/// silo-wide default from <c>EdictOptions.IdempotencyWindowSize</c>, while a
/// per-grain override still wins — a high-throughput singleton consumer can
/// pick its own window without changing the operator-picked default for
/// everyone else.
/// </summary>
public abstract class IdempotencyWindowSizeScenarios<TFixture>
    where TFixture : IdempotencyWindowSizeFixture
{
    readonly TFixture _fixture;

    protected IdempotencyWindowSizeScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WindowSize_ShouldComeFromConfiguredOptions_WhenSubclassDoesNotOverride()
    {
        var probe = _fixture.GrainFactory
            .GetGrain<IIdempotencyWindowSizeDefaultProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.Equal(_fixture.ConfiguredWindowSize, effectiveWindow);
    }

    [Fact]
    public async Task WindowSize_ShouldHonourPerGrainOverride_OverConfiguredOptions()
    {
        var probe = _fixture.GrainFactory
            .GetGrain<IIdempotencyWindowSizeOverrideProbe>(Guid.NewGuid());

        var effectiveWindow = await probe.GetEffectiveWindowSizeAsync();

        Assert.NotEqual(_fixture.ConfiguredWindowSize, effectiveWindow);
        Assert.Equal(7, effectiveWindow);
    }
}
