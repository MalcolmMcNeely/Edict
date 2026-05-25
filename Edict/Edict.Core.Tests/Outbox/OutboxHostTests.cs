using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.Time.Testing;

using Orleans.Streams;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

public sealed class OutboxHostTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    static readonly EdictOptions Options = new();

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task DrainAsync_ShouldEmitExactlyTwoStateWrites_RegardlessOfPendingCount(int eventCount)
    {
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new SuccessfulExecutor());

        var entries = Enumerable.Range(0, eventCount).Select(i => Entry(i)).ToArray();

        await host.EnqueueAndDrainAsync(entries);

        var writeCount = log.Entries.Count(e => e.Method == "WriteStateAsync");
        Assert.Equal(2, writeCount);
    }

    [Fact]
    public async Task DrainAsync_ShouldWalkPendingForwardOnce_SkippingGatedEntryWithoutRevisit()
    {
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var executor = new RecordingExecutor();
        var host = BuildHost(state, log, executor);

        var entryA = Entry(1);
        var entryB = Entry(2);
        var entryC = Entry(3) with { NextAttemptUtc = Now.AddMinutes(5) };
        var entryD = Entry(4);
        var entryE = Entry(5);

        await host.EnqueueAndDrainAsync([entryA, entryB, entryC, entryD, entryE]);

        await Verify(executor.Invocations.Select(i => i.EntryId).ToArray()).DontScrubGuids();
    }

    [Fact]
    public async Task DrainAsync_CleanDrain_ShouldWriteThenUnregisterReminder()
    {
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new SuccessfulExecutor());

        state.State.Outbox = state.State.Outbox
            .Enqueue(Entry(1))
            .Enqueue(Entry(2));

        await host.ReceiveReminderAsync();

        var lastWrite = log.LastIndexOf("WriteStateAsync");
        var unregister = log.LastIndexOf("UnregisterReminderAsync");
        Assert.True(unregister >= 0, "expected UnregisterReminderAsync to be recorded");
        Assert.True(lastWrite < unregister, $"expected last WriteStateAsync ({lastWrite}) before UnregisterReminderAsync ({unregister}); log={Describe(log)}");
    }

    [Fact]
    public async Task DrainAsync_LeavingGatedEntries_ShouldWriteThenRegisterReminder()
    {
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var host = BuildHost(state, log, new SuccessfulExecutor());

        await host.EnqueueAndDrainAsync(
        [
            Entry(1),
            Entry(2) with { NextAttemptUtc = Now.AddMinutes(5) },
        ]);

        var lastWrite = log.LastIndexOf("WriteStateAsync");
        var register = log.LastIndexOf("RegisterOrUpdateReminderAsync");
        Assert.True(register >= 0, "expected RegisterOrUpdateReminderAsync to be recorded");
        Assert.True(lastWrite < register, $"expected last WriteStateAsync ({lastWrite}) before RegisterOrUpdateReminderAsync ({register}); log={Describe(log)}");
    }

    static OutboxHost<EdictUnit> BuildHost(
        CountingPersistentState<GrainEnvelope<EdictUnit>> state,
        CallLog log,
        IOutboxEffectExecutor executor)
    {
        var time = new FakeTimeProvider(Now);
        var reminders = new RecordingReminderRegistrar(log);
        return new OutboxHost<EdictUnit>(
            state,
            NullStreamProvider.Instance,
            reminders,
            [executor],
            Options,
            time,
            new NoopPromoter(),
            grainKey: "test-grain",
            grainTypeName: "TestGrain");
    }

    static OutboxEntry Entry(int seed) => new()
    {
        EntryId = new Guid(seed, (short)0, (short)0, 0, 0, 0, 0, 0, 0, 0, 0),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [(byte)seed],
    };

    static string Describe(CallLog log) =>
        string.Join(", ", log.Entries.Select((e, i) => $"[{i}]{e.SourceTag}.{e.Method}"));

    sealed class SuccessfulExecutor : IOutboxEffectExecutor
    {
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;
        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType) =>
            Task.CompletedTask;
    }

    sealed class RecordingExecutor : IOutboxEffectExecutor
    {
        readonly List<OutboxEntry> _invocations = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;
        public IReadOnlyList<OutboxEntry> Invocations => _invocations;

        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch, Type? consumerType)
        {
            _invocations.Add(entry);
            return Task.CompletedTask;
        }
    }

    sealed class NoopPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now) =>
            failed with { Kind = OutboxEffectKind.PublishEvent };
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
