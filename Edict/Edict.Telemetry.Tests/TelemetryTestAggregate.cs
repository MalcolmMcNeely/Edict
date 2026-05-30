using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Telemetry.Tests;

public sealed partial record TelPlaceOrderCommand(Guid OrderId, string Sku) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

public sealed partial record TelFailOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[EdictStream("TelOrders")]
public sealed partial record TelOrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string Sku { get; init; } = Sku;
}

public partial class TelOrderCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> HandleAsync(TelPlaceOrderCommand command)
    {
        Raise(new TelOrderPlacedEvent(command.OrderId, command.Sku));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task<EdictCommandResult> HandleAsync(TelFailOrderCommand command) =>
        throw new InvalidOperationException("simulated failure");
}

public partial class TelOrderPlacedHandler : EdictEventHandler
{
    public Task HandleAsync(TelOrderPlacedEvent _) => Task.CompletedTask;
}

public interface ITelOrderEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("TelOrders")]
public sealed class TelOrderEventCaptureGrain : Grain, ITelOrderEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

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
