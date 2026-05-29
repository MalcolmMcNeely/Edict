using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Idempotency;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Sagas;

/// <summary>
/// Base for a saga: an idempotent consumer that coordinates a multi-step
/// cross-aggregate workflow, reacting to Events and issuing exactly one Command
/// per Event while holding durable <see cref="Progress"/>. It closes
/// the generic idempotency root on <typeparamref name="TProgress"/>, so the
/// "Event Handlers, Sagas, and Projection Builders all inherit
/// <see cref="EdictIdempotencyBase{TPayload}"/>" relationship — and brand
/// clause (b) — stays literally true.
/// <para>
/// The dedup ring, the outbound Command (a <see cref="OutboxEffectKind.SendCommand"/>
/// effect), and <see cref="Progress"/> commit atomically in the one
/// grain-document write (the inherited <see cref="EdictIdempotencyBase{TPayload}.CollectPendingOutboxEntries"/>
/// hook routes the commit through the Outbox engine), so a crash mid-workflow
/// cannot desynchronise progress from the command it implies.
/// </para>
/// <para>
/// <see cref="Dispatch"/> is a deliberately single-command API: command fan-out
/// from a saga is a coordination smell, so a second call within one event
/// handler is a hard runtime error — structurally unmissable rather than
/// advisory, and a deliberate asymmetry with the Command Handler's buffering
/// <c>Raise</c> (no analyzer, consistent with <c>Raise</c> having none).
/// </para>
/// </summary>
public abstract class EdictSaga<TProgress> : EdictIdempotencyBase<TProgress>, IEdictSaga
    where TProgress : IEdictPersistedState, new()
{
    readonly SagaDispatchBuffer _dispatchBuffer = new();
    OutboxEntry? _stagedEntry;
    Serializer? _cachedSerializer;
    IEdictMetricsCache? _cachedMetricsCache;
    TimeProvider? _cachedTimeProvider;
    string? _cachedSagaType;
    string? _cachedSagaKey;

    /// <summary>
    /// Durable workflow progress. The consumer mutates this inside a
    /// <c>Handle</c>; it is the payload slot of the persisted envelope,
    /// committed atomically with the dedup ring and any dispatched command.
    /// </summary>
    protected TProgress Progress => State.Payload;

    /// <inheritdoc cref="IEdictSaga.GetEdictProgressAsync" />
    public Task<object> GetEdictProgressAsync() => Task.FromResult<object>(Progress!);

    /// <summary>
    /// Issues the single Command this Event implies. Buffered now and staged as
    /// the <see cref="OutboxEffectKind.SendCommand"/> effect after the handler
    /// succeeds, so it commits atomically with the ring and
    /// <see cref="Progress"/>. Calling this a second time within one event
    /// handler throws — a saga that fans out commands is a coordination smell,
    /// and the single-command API shape makes that constraint structural.
    /// </summary>
    protected void Dispatch(EdictCommand command) => _dispatchBuffer.Set(command);

    /// <summary>
    /// Generator-only fast path called by the per-type saga Dispatch
    /// interceptor stubs (ADR-0034). Identical semantics to
    /// <see cref="Dispatch"/> on the typed argument — the win is a
    /// monomorphic typed call site. Not a stable public API; the interceptor
    /// emitter is the only caller. The single-command-per-event invariant
    /// (<see cref="SagaDispatchBuffer.Set"/> throws on a second call) still
    /// holds.
    /// </summary>
    public void DispatchFast<TCommand>(TCommand command) where TCommand : EdictCommand
        => _dispatchBuffer.Set(command);

    /// <summary>
    /// Resets the single outbound-command buffer before each handler so the
    /// one-command-per-event limit is scoped to one Event, then runs the
    /// handler. The buffered command is collected by
    /// <see cref="CollectPendingOutboxEntries"/> after the handler succeeds.
    /// </summary>
    protected override async Task DispatchEventAsync<TEvent>(TEvent edictEvent, Func<TEvent, Task> handler)
    {
        _dispatchBuffer.Reset();
        _stagedEntry = null;

        await handler(edictEvent);

        // Build the SendCommand entry here, while the handle span is still
        // Activity.Current, so its captured traceparent makes the dispatched
        // command nest under the saga handle span as parent-child even when a
        // crash-recovery drain runs much later. CollectPendingOutboxEntries
        // runs after the span has been disposed, so capturing there would orphan
        // the command span.
        var command = _dispatchBuffer.Take();
        if (command is not null)
        {
            _stagedEntry = BuildSendCommandEntry(command);
        }

        ReportSagaProgress();
    }

    void ReportSagaProgress()
    {
        var cache = _cachedMetricsCache ??= ServiceProvider.GetService<IEdictMetricsCache>();
        if (cache is null)
        {
            return;
        }
        var time = _cachedTimeProvider ??= ServiceProvider.GetRequiredService<TimeProvider>();
        cache.ReportSaga(
            sagaType: _cachedSagaType ??= GetType().FullName ?? GetType().Name,
            sagaKey: _cachedSagaKey ??= this.GetPrimaryKey().ToString(),
            lastHandledAt: time.GetUtcNow());
    }

    /// <inheritdoc />
    protected override IReadOnlyList<OutboxEntry> CollectPendingOutboxEntries()
    {
        if (_stagedEntry is null)
        {
            return [];
        }

        var entry = _stagedEntry;
        _stagedEntry = null;
        return [entry];
    }

    OutboxEntry BuildSendCommandEntry(EdictCommand command)
    {
        var current = Activity.Current;
        var traceParent = current?.BuildTraceParent();

        var serializer = _cachedSerializer ??= ServiceProvider.GetRequiredService<Serializer>();

        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.SendCommand,
            Payload = serializer.SerializeToArray<EdictCommand>(command),
            TraceParent = traceParent,
            TraceState = current?.TraceStateString,
        };
    }
}
