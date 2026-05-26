using Edict.Contracts.Events;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Events;

/// <summary>
/// Substrate-neutral subscriber on the conformance event stream. The
/// conformance event-publishing scenarios drive
/// <see cref="PlaceOrderCommand"/> through the bound substrate's pipeline and
/// then read back from this grain to assert what actually landed on the
/// stream. The grain itself is provider-agnostic — only the stream provider
/// behind <c>"edict"</c> varies between substrates.
/// </summary>
public interface IOrderEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("ConformanceOrders")]
public sealed class OrderEventCaptureGrain : Grain, IOrderEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceOrders", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}
