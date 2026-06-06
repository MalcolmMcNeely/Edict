using Edict.Testing;

using Sample.Contracts.Delivery.Commands;
using Sample.Contracts.Delivery.Projections;
using Sample.Domain.Delivery.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Delivery;

/// <summary>
/// Per-tick tests for <see cref="DeliveryStatusRow"/>, the in-grain State
/// projection the Schedules Delivery sub-section reads to show the ETA ticking
/// down. Each interval-agnostic FireDueSchedulesAsync drives one recurring fire
/// whose DeliveryEtaTickedEvent mutates the in-grain projection; arrival flips
/// Delivered. The read goes through <see cref="EdictTestApp.ReadProjectionAsync{TProjection}"/>,
/// the consumer reader seam, not grain storage.
/// </summary>
public sealed class DeliveryStatusProjectionBuilderTests
{
    [Fact]
    public async Task EachFireTicksTheProjectedEtaDown()
    {
        var orderId = Guid.Parse("d1000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DeliveryTrackerCommandHandler).Assembly));

        await app.SendAsync(new StartDeliveryTrackingCommand(orderId, EtaDays: 3));
        await app.Drain();

        await app.FireDueSchedulesAsync();
        await app.Drain();

        var projection = await app.ReadProjectionAsync<DeliveryStatusRow>(orderId);
        Assert.NotNull(projection);
        Assert.Equal(2, projection.EtaDaysRemaining);
        Assert.False(projection.Delivered);
    }

    [Fact]
    public async Task ArrivalMarksTheRowDelivered()
    {
        var orderId = Guid.Parse("d1000000-0000-0000-0000-000000000002");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(DeliveryTrackerCommandHandler).Assembly));

        await app.SendAsync(new StartDeliveryTrackingCommand(orderId, EtaDays: 2));
        await app.Drain();

        await app.FireDueSchedulesAsync();
        await app.FireDueSchedulesAsync();
        await app.Drain();

        var projection = await app.ReadProjectionAsync<DeliveryStatusRow>(orderId);
        Assert.NotNull(projection);
        Assert.Equal(0, projection.EtaDaysRemaining);
        Assert.True(projection.Delivered);
    }
}
