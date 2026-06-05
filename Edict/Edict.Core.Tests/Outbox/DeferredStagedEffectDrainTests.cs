using Edict.Contracts;
using Edict.Contracts.Commands;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Core.Sagas;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.Saga;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.Time.Testing;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

// The deferred-dispatch fix: a saga / table-projection builder that receives a
// claim-checked event via the InvokeHandler drain stages its downstream effect
// through the dispatch return value, which the drain restages atomically with
// the InvokeHandler ack. These host-level tests exercise that restaging,
// closing the lost-effect, clobbered-effect, and exactly-once gaps.
public sealed class DeferredStagedEffectDrainTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();

    [Fact]
    public async Task Drain_DeferredDispatchStagesEffect_RestagesAndDrainsItInSamePass()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var sendExecutor = new RecordingExecutor(OutboxEffectKind.SendCommand);
        var host = BuildHost(
            state, log,
            [new StagingInvokeHandlerExecutor(), sendExecutor],
            deferredDispatch: edictEvent => Task.FromResult<OutboxEntry?>(SendCommandEntry(edictEvent.EventId)));

        // Act
        await host.EnqueueAndDrainAsync([InvokeEntry(Guid.NewGuid())]);

        // Assert
        Assert.Single(sendExecutor.Executed);
        Assert.Empty(state.State.Outbox.Pending);
    }

    [Fact]
    public async Task Drain_ConcurrentDeferredInvokeHandlers_ProducesEveryStagedEffectAndClobbersNone()
    {
        // Arrange
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var sendExecutor = new RecordingExecutor(OutboxEffectKind.SendCommand);
        var saga = new FakeSagaDispatch();
        var host = BuildHost(
            state, log,
            [new StagingInvokeHandlerExecutor(), sendExecutor],
            deferredDispatch: saga.DispatchAsync);

        var intakeIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

        // Act — eight pending InvokeHandler entries drain concurrently; each
        // deferred dispatch yields, so they interleave on the scheduler.
        await host.EnqueueAndDrainAsync(intakeIds.Select(InvokeEntry).ToArray());

        // Assert — every dispatch's command surfaced exactly once, keyed back to
        // its own intake event, with no cross-wiring and no spurious throw.
        var stagedSourceIds = sendExecutor.Executed.Select(e => new Guid(e.Payload)).ToHashSet();
        Assert.Equal(intakeIds.ToHashSet(), stagedSourceIds);
        Assert.Empty(state.State.Outbox.Pending);
    }

    [Fact]
    public async Task Drain_WriteFaultAfterDispatch_LeavesInvokeHandlerPendingAndReRunsEffectExactlyOnce()
    {
        // Arrange — fail the composed ack-write (the second write: the first is
        // the initial enqueue of the InvokeHandler entry).
        var log = new CallLog();
        var state = new FaultInjectingPersistentState { FailOnWrite = 2 };
        var sendExecutor = new RecordingExecutor(OutboxEffectKind.SendCommand);

        OutboxHost<EdictUnit> BuildOver(FaultInjectingPersistentState backing) => BuildHost(
            backing, log,
            [new StagingInvokeHandlerExecutor(), sendExecutor],
            deferredDispatch: edictEvent => Task.FromResult<OutboxEntry?>(SendCommandEntry(edictEvent.EventId)));

        var intakeId = Guid.NewGuid();

        // Act — the fault on the composed ack-write propagates out of the drain.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildOver(state).EnqueueAndDrainAsync([InvokeEntry(intakeId)]));

        // Assert — the handler ran but the effect never executed (it would drain
        // only after the ack-write that faulted), and the InvokeHandler entry is
        // still durably Pending, so the effect is not lost.
        Assert.Empty(sendExecutor.Executed);
        Assert.Contains(state.Durable.Outbox.Pending, e => e.Kind == OutboxEffectKind.InvokeHandler);

        // Act — reactivate from the durable snapshot and drain again cleanly.
        state.Reactivate();
        await BuildOver(state).DrainAsync();

        // Assert — the staged effect drained exactly once across the fault and
        // recovery, and the outbox is clean.
        Assert.Single(sendExecutor.Executed);
        Assert.Empty(state.State.Outbox.Pending);
    }

    [Fact]
    public async Task Drain_DeferredDispatchStagesEffect_AcksInvokeHandlerAndEnqueuesEffectInOneWrite()
    {
        // Arrange
        var log = new CallLog();
        var state = new CapturingPersistentState();
        var sendExecutor = new RecordingExecutor(OutboxEffectKind.SendCommand);
        var host = BuildHost(
            state, log,
            [new StagingInvokeHandlerExecutor(), sendExecutor],
            deferredDispatch: edictEvent => Task.FromResult<OutboxEntry?>(SendCommandEntry(edictEvent.EventId)));

        // Act
        await host.EnqueueAndDrainAsync([InvokeEntry(Guid.NewGuid())]);

        // Assert — the first persisted write that no longer carries the
        // InvokeHandler entry already carries the staged SendCommand: the ack
        // and the effect enqueue land in one write, with no two-write window.
        var ackWrite = state.WriteSnapshots.First(snapshot =>
            snapshot.All(kind => kind != OutboxEffectKind.InvokeHandler));
        Assert.Contains(OutboxEffectKind.SendCommand, ackWrite);
    }

    static OutboxHost<EdictUnit> BuildHost(
        IPersistentState<GrainEnvelope<EdictUnit>> state,
        CallLog log,
        IReadOnlyList<IOutboxEffectExecutor> executors,
        Func<EdictEvent, Task<OutboxEntry?>> deferredDispatch) =>
        new(
            state,
            NullStreamProvider.Instance,
            new RecordingReminderRegistrar(log),
            executors,
            Options,
            new FakeTimeProvider(Now),
            new NoopPromoter(),
            grainKey: "test-grain",
            grainTypeName: "TestGrain",
            deferredDispatch: deferredDispatch);

    static OutboxEntry InvokeEntry(Guid entryId) => new()
    {
        EntryId = entryId,
        Kind = OutboxEffectKind.InvokeHandler,
        Payload = [],
    };

    static OutboxEntry SendCommandEntry(Guid sourceEventId) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.SendCommand,
        // Carry the source event id so a test can prove which dispatch staged it.
        Payload = sourceEventId.ToByteArray(),
    };

    // Mirrors the production InvokeHandler executor's seam: materialise the
    // event (here synthesised from the entry id so each dispatch is distinct)
    // and forward to the deferred-dispatch callback, returning whatever effect
    // it staged.
    sealed class StagingInvokeHandlerExecutor : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

        public Task<OutboxEntry?> ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent) =>
            deferredDispatch!(new CounterIncrementedEvent(entry.EntryId, NewCount: 1) { EventId = entry.EntryId });
    }

    // Exercises the real invocation-scoped saga staging: every dispatch opens
    // its own SagaDispatchBuffer through the InvocationScope, yields so
    // concurrent dispatches interleave, dispatches one command, and stages it.
    // A shared buffer would throw EdictSagaCoordinationException or cross-wire
    // here; the AsyncLocal scope keeps each dispatch isolated.
    sealed class FakeSagaDispatch
    {
        readonly InvocationScope<SagaDispatchBuffer> _scope = new();

        public async Task<OutboxEntry?> DispatchAsync(EdictEvent edictEvent)
        {
            var buffer = _scope.Begin();
            await Task.Yield();
            buffer.Set(new SagaTrackerCommand(edictEvent.EventId));
            await Task.Yield();

            var command = buffer.Take();
            return command is SagaTrackerCommand dispatched
                ? SendCommandEntry(dispatched.WorkflowId)
                : null;
        }
    }

    sealed class RecordingExecutor(OutboxEffectKind kind) : IOutboxEffectExecutor
    {
        readonly List<OutboxEntry> _executed = [];

        public IReadOnlyList<OutboxEntry> Executed => _executed;
        public OutboxEffectKind Kind => kind;

        public Task<OutboxEntry?> ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch, Type? consumerType, EdictEvent? liveWireEvent)
        {
            _executed.Add(entry);
            return Task.FromResult<OutboxEntry?>(null);
        }
    }

    // Persists into a durable snapshot only on a successful write, throwing on a
    // chosen write so a crash mid-drain can be simulated. The Outbox slice is
    // immutable (transitions reassign the slot), so capturing the reference is a
    // faithful point-in-time snapshot.
    sealed class FaultInjectingPersistentState : IPersistentState<GrainEnvelope<EdictUnit>>
    {
        int _writes;

        public int FailOnWrite { get; init; }
        public GrainEnvelope<EdictUnit> State { get; set; } = new();
        public GrainEnvelope<EdictUnit> Durable { get; private set; } = new();

        public string Etag => string.Empty;
        public bool RecordExists => true;

        public Task WriteStateAsync()
        {
            _writes++;
            if (_writes == FailOnWrite)
            {
                throw new InvalidOperationException("simulated write fault");
            }

            Durable = Clone(State);
            return Task.CompletedTask;
        }

        public void Reactivate() => State = Clone(Durable);

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;

        static GrainEnvelope<EdictUnit> Clone(GrainEnvelope<EdictUnit> source) => new()
        {
            Payload = source.Payload,
            Outbox = source.Outbox,
            Idempotency = source.Idempotency,
        };
    }

    // Records the kinds present in Pending at each write so a test can assert
    // which mutations land together in one write.
    sealed class CapturingPersistentState : IPersistentState<GrainEnvelope<EdictUnit>>
    {
        readonly List<IReadOnlyList<OutboxEffectKind>> _writeSnapshots = [];

        public IReadOnlyList<IReadOnlyList<OutboxEffectKind>> WriteSnapshots => _writeSnapshots;
        public GrainEnvelope<EdictUnit> State { get; set; } = new();

        public string Etag => string.Empty;
        public bool RecordExists => true;

        public Task WriteStateAsync()
        {
            _writeSnapshots.Add(State.Outbox.Pending.Select(entry => entry.Kind).ToArray());
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
    }

    sealed class NoopPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now) =>
            failed with { Kind = OutboxEffectKind.PublishEvent };

        public OutboxEntry PromoteScheduleTimeout(string scheduleMessageType, string sourceGrainKey, string sourceGrainType, string? traceParent, string? traceState, DateTimeOffset now) =>
            throw new NotSupportedException();
    }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("NullStreamProvider has no streams.");
    }
}
