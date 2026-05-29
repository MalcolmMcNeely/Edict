using Edict.Contracts.Events;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

public interface ICounterEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

/// <summary>
/// Test-only grain: subscribes to the "Counters" domain stream and buffers
/// every event so Outbox engine tests can assert what the inline drain
/// published.
/// </summary>
[ImplicitStreamSubscription("Counters")]
public sealed class CounterEventCaptureGrain : Grain, ICounterEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("Counters", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}
