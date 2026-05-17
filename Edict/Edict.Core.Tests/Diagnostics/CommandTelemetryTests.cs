using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Core.Diagnostics;

namespace Edict.Core.Tests.Diagnostics;

[Collection(EdictClusterCollection.Name)]
public sealed class CommandTelemetryTests(EdictClusterFixture fixture)
{
    [Fact]
    public async Task Send_opens_one_edict_span_per_command_dispatch()
    {
        var orderId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var span = stopped.Single(activity => orderId.Equals(activity.GetTagItem("edict.command.route_key")));
        Assert.Equal("edict.command PlaceOrderCommand", span.OperationName);
    }

    [Fact]
    public async Task Send_records_error_on_span_when_handler_throws()
    {
        var orderId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);

        await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Sender.Send(new FailOrderCommand(orderId)));

        var span = stopped.Single(activity => orderId.Equals(activity.GetTagItem("edict.command.route_key")));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task Send_writes_telemeterized_properties_as_edict_tags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-TELEM-1";
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, sku));

        var span = stopped.Single(activity => orderId.Equals(activity.GetTagItem("edict.command.route_key")));
        Assert.Equal(sku, span.GetTagItem("edict.placeordercommand.sku"));
    }
}
