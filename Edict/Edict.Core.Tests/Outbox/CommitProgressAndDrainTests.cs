using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Idempotency;
using Edict.Core.Outbox;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.Time.Testing;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Outbox;

// The consumer dedup commit and any staged effect ride one atomic grain-state
// write; the in-memory mirror is confirmed only after that write lands; and a
// write fault rolls both the ring and the staged effect back so a redelivery is
// re-dispatched rather than suppressed against an id the store never persisted.
// Drives the host's commit boundary directly, the way EdictIdempotencyBase does.
public sealed class CommitProgressAndDrainTests
{
    static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();
    static readonly Guid HandledEventId = new("11111111-0000-0000-0000-000000000001");

    [Fact]
    public async Task CommitProgressAndDrain_WriteFault_RollsBackRingAndEffectAndLeavesMirrorEmpty()
    {
        // Arrange
        var state = new FaultInjectingPersistentState { FailOnWrite = 1 };
        state.State.Idempotency.HandledEventIds = new Guid[4];
        var mirror = new DedupRingMirror();
        mirror.Activate(state.State.Idempotency.HandledEventIds, head: 0, count: 0);

        var sendExecutor = new RecordingExecutor(OutboxEffectKind.SendCommand);
        var host = BuildHost(state, [sendExecutor]);

        DedupRing.Revert revert = default;

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CommitProgressAndDrainAsync(
            applyProgress: () =>
            {
                revert = DedupRing.Apply(state.State.Idempotency, windowSize: 4, HandledEventId);
                mirror.Commit(HandledEventId);
            },
            rollbackProgress: () =>
            {
                DedupRing.RollBack(state.State.Idempotency, revert);
                mirror.Activate(state.State.Idempotency.HandledEventIds, state.State.Idempotency.Head, state.State.Idempotency.Count);
            },
            stagedEffect: SendCommandEntry()));

        // Assert — the optimistic mirror claim was reverted, the ring rolled
        // back, the effect rolled back, and nothing reached durable storage.
        Assert.False(mirror.Contains(HandledEventId));
        Assert.Equal(0, state.State.Idempotency.Count);
        Assert.Empty(state.State.Outbox.Pending);
        Assert.Empty(sendExecutor.Executed);
        Assert.Equal(0, state.Durable.Idempotency.Count);
        Assert.DoesNotContain(HandledEventId, state.Durable.Idempotency.HandledEventIds);
    }

    [Fact]
    public async Task CommitProgressAndDrain_WriteSucceeds_ConfirmsMirrorPersistsRingAndDrainsEffect()
    {
        // Arrange
        var state = new FaultInjectingPersistentState();
        state.State.Idempotency.HandledEventIds = new Guid[4];
        var mirror = new DedupRingMirror();
        mirror.Activate(state.State.Idempotency.HandledEventIds, head: 0, count: 0);

        var sendExecutor = new RecordingExecutor(OutboxEffectKind.SendCommand);
        var host = BuildHost(state, [sendExecutor]);

        DedupRing.Revert revert = default;

        // Act
        await host.CommitProgressAndDrainAsync(
            applyProgress: () =>
            {
                revert = DedupRing.Apply(state.State.Idempotency, windowSize: 4, HandledEventId);
                mirror.Commit(HandledEventId);
            },
            rollbackProgress: () =>
            {
                DedupRing.RollBack(state.State.Idempotency, revert);
                mirror.Activate(state.State.Idempotency.HandledEventIds, state.State.Idempotency.Head, state.State.Idempotency.Count);
            },
            stagedEffect: SendCommandEntry());

        // Assert — the id is durably persisted, the mirror reflects it, the
        // staged effect drained, and the outbox is clean.
        Assert.True(mirror.Contains(HandledEventId));
        Assert.Equal(1, state.Durable.Idempotency.Count);
        Assert.Equal(HandledEventId, state.Durable.Idempotency.HandledEventIds[0]);
        Assert.Single(sendExecutor.Executed);
        Assert.Empty(state.State.Outbox.Pending);
    }

    static OutboxHost<EdictUnit> BuildHost(
        IPersistentState<GrainEnvelope<EdictUnit>> state,
        IReadOnlyList<IOutboxEffectExecutor> executors) =>
        new(
            state,
            NullStreamProvider.Instance,
            new RecordingReminderRegistrar(new CallLog()),
            executors,
            Options,
            new FakeTimeProvider(Now),
            new NoopPromoter(),
            grainKey: "test-grain",
            grainTypeName: "TestGrain");

    static OutboxEntry SendCommandEntry() => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.SendCommand,
        Payload = [],
    };

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
