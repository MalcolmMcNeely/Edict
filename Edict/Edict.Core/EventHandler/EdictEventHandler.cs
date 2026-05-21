using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.EventHandler;

/// <summary>
/// Consumer-facing base for the "Event Handler" role: an
/// idempotent consumer whose stream-callback path stages a deferred
/// <see cref="OutboxEffectKind.InvokeHandler"/> Outbox entry instead of running
/// the consumer's <c>Handle(TEvent)</c> inline. The actual invocation runs
/// later through the composed <see cref="OutboxHost{TPayload}"/> — picking up
/// its per-entry retry/backoff and dead-letter promotion for free, so
/// transient external-I/O failures are framework-managed and permanent
/// failures land on the queryable dead-letter projection.
/// <para>
/// Authoring shape is identical to <c>EdictProjectionBuilder</c>: a
/// <c>partial</c> class with one <c>Handle(TEvent)</c> overload per event type,
/// the generator emits the Orleans interface, the implicit-stream-subscription
/// attribute, and both <see cref="DispatchAsync"/> (used at drain time) and
/// <see cref="HandlesType"/> (used at stream-callback time to keep unhandled
/// event types from consuming a dedup ring slot on the inline branch).
/// </para>
/// <para>
/// The role is <b>terminal</b>: there is no <c>Raise</c> (events belong to
/// aggregates), no <c>Dispatch</c> (sagas own command dispatch). Failure
/// classification (transient vs permanent) is the consumer's domain decision
/// inside <c>Handle</c>; every throw is treated uniformly as transient → exp
/// backoff → dead-letter at <c>MaxAttempts</c>. External-API idempotency is
/// the consumer's responsibility — the canonical pattern is to pass
/// <see cref="EdictEvent.EventId"/> as the API's idempotency key.
/// </para>
/// </summary>
public abstract class EdictEventHandler : EdictIdempotencyBase
{
    /// <summary>
    /// Synchronous pre-flight emitted by the generator: <c>true</c> if the
    /// concrete subclass has a matching <c>Handle(TEvent)</c> overload for
    /// <paramref name="evt"/>. The stream-callback path checks this before
    /// staging an Outbox entry so an unhandled event type is a pure no-op and
    /// does not consume a dedup ring slot.
    /// </summary>
    protected abstract bool HandlesType(EdictEvent evt);

    /// <inheritdoc />
    protected override async Task OnStreamEventAsync(EdictEvent evt, StreamSequenceToken? _)
    {
        EnsureWindowInitialized();

        if (Contains(evt.EventId))
        {
            EmitDedupSpan(evt);
            return;
        }

        if (!HandlesType(evt))
        {
            // Unhandled types are a pure no-op: no ring slot consumed,
            // no Outbox entry staged. Keeps the dedup window for events
            // this handler actually handles.
            return;
        }

        Commit(evt.EventId);

        var entry = BuildInvokeHandlerEntry(evt);
        await Host.EnqueueAndDrainAsync([entry]);
    }

    OutboxEntry BuildInvokeHandlerEntry(EdictEvent evt)
    {
        // The deferred-invocation span — opened later by InvokeHandlerExecutor —
        // must nest under the publish span as parent-child even when backoff
        // defers the call. The event itself carries the publish
        // span's identity in its TraceId/SpanId fields (stamped by
        // PublishEventExecutor) so the parent is recoverable even when the
        // stream transport did not propagate Activity.Current across the hop
        // — the case Azure Queue streams hit. Fall back to Activity.Current
        // (e.g. an in-process publisher that bypassed the publish executor)
        // only when the event carries no embedded trace context.
        string? traceParent;
        string? traceState;
        if (evt.TraceId is { Length: 32 } eventTraceId && evt.SpanId is { Length: 16 } eventSpanId)
        {
            traceParent = ActivityExtensions.BuildTraceParent(eventTraceId, eventSpanId);
            traceState = evt.TraceState;
        }
        else if (Activity.Current is { } current)
        {
            traceParent = ActivityExtensions.BuildTraceParent(
                current.TraceId.ToHexString(), current.SpanId.ToHexString());
            traceState = current.TraceStateString;
        }
        else
        {
            traceParent = null;
            traceState = null;
        }

        var serializer = ServiceProvider.GetRequiredService<Serializer>();

        // InvokeHandler entry payloads are serialised EdictEventEnvelopes
        // (inline or pointer). The inline-branch case the EventHandler
        // stream-callback hits gets wrapped here; the executor unwraps
        // via ClaimCheckUnwrap before dispatching.
        var envelope = evt is EdictEventEnvelope already
            ? already
            : EnvelopeCodec.WrapInline(serializer.SerializeToArray<EdictEvent>(evt));

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = serializer.SerializeToArray<EdictEvent>(envelope),
            TraceParent = traceParent,
            TraceState = traceState,
        };
    }
}
