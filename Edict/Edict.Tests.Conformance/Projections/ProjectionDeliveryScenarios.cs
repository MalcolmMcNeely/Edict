using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Projections;

/// <summary>
/// Substrate-agnostic conformance for the in-memory projection delivery
/// path: an accepted command publishes an event that lands on the projection
/// grain identified by the aggregate route key. Lifted from
/// <c>ProjectionBuilderTests.HandleAsync_ShouldDeliverEventToProjectionGrain_WhenCommandIsAccepted</c>.
/// </summary>
public abstract class ProjectionDeliveryScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected ProjectionDeliveryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldDeliverEventToProjectionGrain_WhenCommandIsAccepted()
    {
        var orderId = Guid.NewGuid();

        await _fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var projection = _fixture.GrainFactory.GetGrain<IOrderProjectionAccess>(orderId);
        await WaitForProjectionAsync(projection, expectedCount: 1);
        Assert.Equal(1, await projection.GetOrderCountAsync());
    }

    static async Task WaitForProjectionAsync(IOrderProjectionAccess projection, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await projection.GetOrderCountAsync() >= expectedCount)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
