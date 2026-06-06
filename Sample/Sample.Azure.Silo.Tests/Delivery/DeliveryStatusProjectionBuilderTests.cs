using Edict.Testing;

using Sample.Contracts.Delivery.Commands;
using Sample.Contracts.Delivery.Projections;
using Sample.Domain.Delivery.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Delivery;

/// <summary>
/// Per-tick tests for <see cref="DeliveryStatusRow"/>, the read model the Schedules
/// Delivery sub-section reads to show the ETA ticking down. Each interval-agnostic
/// FireDueSchedulesAsync drives one recurring fire whose DeliveryEtaTickedEvent
/// lands on the row; arrival flips Delivered.
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

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.Equal(2, row.EtaDaysRemaining);
        Assert.False(row.Delivered);
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

        var row = await GetRow(app, orderId);
        Assert.NotNull(row);
        Assert.Equal(0, row.EtaDaysRemaining);
        Assert.True(row.Delivered);
    }

    static Task<DeliveryStatusRow?> GetRow(EdictTestApp app, Guid orderId) =>
        app.GetProjectionRow<DeliveryStatusRow>(
            tableName: "deliverystatus",
            partitionKey: orderId.ToString(),
            rowKey: "delivery");
}
