using Edict.Tests.Conformance.Projections;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the projection dispatcher's no-op path: an
/// event type the projection does not handle, delivered on the stream, must leave
/// the projection state untouched.
/// </summary>
public abstract class ProjectionUnhandledEventScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected ProjectionUnhandledEventScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldBeNoOp_WhenEventTypeIsUnhandled()
    {
        var grainId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IStreamPublisher>(grainId);
        var projection = _fixture.GrainFactory.GetGrain<IOrderProjectionAccess>(grainId);

        var unhandled = new UnknownOrderEvent(grainId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync("ConformanceOrders", unhandled);

        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await projection.GetOrderCountAsync());
    }
}
