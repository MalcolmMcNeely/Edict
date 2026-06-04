using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Pairing.Tests;

/// <summary>
/// Bucket-4 composition smoke: the documented stack for a shipped pairing — both
/// real providers' DI and serializer registration present — boots and round-trips
/// a command end to end. An accepted command commits grain state on the real
/// persistence provider, raises an event the outbox publishes on the real stream
/// provider, and an implicit subscriber on the other side captures it. Neither
/// single-axis battery can prove this: each fakes one half.
/// </summary>
public abstract class CompositionBootScenarios<TFixture>
    where TFixture : PairingFixture
{
    readonly TFixture _fixture;

    protected CompositionBootScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DocumentedStack_Boots_AndRoundTripsCommandToPublishedEvent()
    {
        var counterId = Guid.NewGuid();

        // Act — the documented composition handles the command (real persistence
        // write) and publishes the raised event over the real stream.
        var result = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — accepted, and the raised event lands on the implicit subscriber
        // across the real stream hop with the round-tripped payload intact.
        Assert.IsType<EdictCommandResult.Accepted>(result);

        var capture = _fixture.GrainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        await PairingWaiters.WaitUntilAsync(async () => (await capture.GetCapturedEventsAsync()).Count == 1);
        var captured = Assert.Single(await capture.GetCapturedEventsAsync());
        var incremented = Assert.IsType<CounterIncrementedEvent>(captured);
        Assert.Equal(1, incremented.NewCount);
    }
}
