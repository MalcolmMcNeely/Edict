using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Telemetry;

using Orleans;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Grains;

/// <summary>
/// Abstract base for every event-consuming grain. Owns the stream-observer
/// callback, suppresses at-least-once redeliveries via a configurable bounded
/// ring of recently seen <see cref="EdictEvent.EventId"/>s persisted in grain
/// state, and commits progress only after the subclass's dispatch succeeds
/// (ADR 0002). Shaped so <c>EventHandlerGrain</c>/<c>SagaGrain</c> can inherit
/// without rework (next slices).
/// </summary>
[StorageProvider(ProviderName = "edict-dedup")]
public abstract class EdictEventDeduplicationGrain : Grain<DeduplicationState>
{
    /// <summary>
    /// Maximum number of distinct <see cref="EdictEvent.EventId"/>s remembered.
    /// Override in the subclass to tune for expected redelivery volume.
    /// </summary>
    protected virtual int RingSize => 100;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await SubscribeToStreamAsync(cancellationToken);
    }

    /// <summary>
    /// Implemented by the concrete subclass to subscribe to its domain stream,
    /// passing <see cref="OnStreamEventAsync"/> as the callback. The base never
    /// decides which stream or provider to use.
    /// </summary>
    protected abstract Task SubscribeToStreamAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Implemented by the concrete subclass (or a future generator) to dispatch
    /// the incoming event to a strongly typed handler. Returns <c>true</c> if
    /// the event was handled (ring slot consumed on success), <c>false</c> if
    /// the event type is not handled by this consumer (no ring slot consumed).
    /// A thrown exception leaves the <see cref="EdictEvent.EventId"/> uncommitted so
    /// Orleans redelivers.
    /// </summary>
    protected abstract Task<bool> DispatchAsync(EdictEvent evt);

    /// <summary>
    /// The dedup-guarded stream callback. Subclasses pass this method to
    /// <c>stream.SubscribeAsync</c> from <see cref="SubscribeToStreamAsync"/>.
    /// </summary>
    protected async Task OnStreamEventAsync(EdictEvent evt, StreamSequenceToken _)
    {
        EnsureRingInitialized();

        if (Contains(evt.EventId))
        {
            EmitDedupSpan(evt);
            return;
        }

        var handled = await DispatchAsync(evt);

        if (handled)
        {
            Commit(evt.EventId);
            await WriteStateAsync();
        }
    }

    private void EnsureRingInitialized()
    {
        if (State.Ring.Length != RingSize)
        {
            State.Ring = new Guid[RingSize];
            State.Head = 0;
            State.Count = 0;
        }
    }

    private bool Contains(Guid eventId)
    {
        if (State.Count < State.Ring.Length)
            return Array.IndexOf(State.Ring, eventId, 0, State.Count) >= 0;
        return Array.IndexOf(State.Ring, eventId) >= 0;
    }

    private void Commit(Guid eventId)
    {
        State.Ring[State.Head] = eventId;
        State.Head = (State.Head + 1) % RingSize;
        if (State.Count < RingSize)
            State.Count++;
    }

    private static void EmitDedupSpan(EdictEvent evt)
    {
        var parentContext = ActivityExtensions.RestoreFromStrings(evt.TraceId, evt.SpanId, evt.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventDeduplicated(
            evt.GetType().Name, parentContext);
        span?.SetTag("edict.deduplicated", true);
    }
}
