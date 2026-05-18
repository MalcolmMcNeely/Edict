using Edict.Contracts.Events;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

public interface IOrderEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<Event>> GetCapturedEventsAsync();
}

/// <summary>
/// Test-only grain: subscribes to the "Orders" domain stream and buffers
/// every event so integration tests can assert on what was published.
/// Activated implicitly when the first event arrives for its key.
/// </summary>
[ImplicitStreamSubscription("Orders")]
public sealed class OrderEventCaptureGrain : Grain, IOrderEventCaptureGrain
{
    private readonly List<Event> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<Event>(StreamId.Create("Orders", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Event>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<Event>>(_events.AsReadOnly());
}
