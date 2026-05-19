using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Telemetry.Tests;

// Minimal consumer-shaped grains for telemetry integration tests.
// The generator runs over this assembly and emits IOrderCommandHandler, Dispatch, and AddEdict().

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record TelPlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record TelFailOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("TelOrders")]
public sealed partial record TelOrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    public string Sku { get; init; } = Sku;
}

public partial class TelOrderCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(TelPlaceOrderCommand command)
    {
        Raise(new TelOrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> Handle(TelFailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");
}

public interface ITelOrderEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("TelOrders")]
public sealed class TelOrderEventCaptureGrain : Grain, ITelOrderEventCaptureGrain
{
    private readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("TelOrders", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}
