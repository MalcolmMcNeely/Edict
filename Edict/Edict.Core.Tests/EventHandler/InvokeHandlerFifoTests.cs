using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using Microsoft.Extensions.Time.Testing;

using Orleans.Runtime;
using Orleans.Streams;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.EventHandler;

// FIFO stop-at-head for InvokeHandler entries per host grain (ADR 0023): a
// transient failure on the head InvokeHandler entry blocks subsequent entries
// from running until backoff retries succeed. The host is kind-agnostic for
// FIFO, so this exercises the same stop-at-head behaviour proven for
// PublishEvent in OutboxHostTests against the new InvokeHandler kind.
public sealed class InvokeHandlerFifoTests
{
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    static OutboxEntry InvokeHandlerEntry(Guid id) => new()
    {
        EntryId = id,
        Kind = OutboxEffectKind.InvokeHandler,
        Payload = [1, 2, 3],
    };

    [Fact]
    public async Task DrainAsync_ShouldStopAtHead_WhenInvokeHandlerEntryFailsTransiently()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new ThrowingInvokeHandlerExecutor();
        var state = new FakePersistentState
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice()
                    .Enqueue(InvokeHandlerEntry(EntryA))
                    .Enqueue(InvokeHandlerEntry(EntryB)),
            },
        };
        var reminders = new FakeReminderRegistrar();
        var options = new EdictOutboxOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromSeconds(2),
            JitterFraction = 0,
        };
        var host = new OutboxHost<EdictUnit>(
            state,
            FakeStreamProvider.Instance,
            reminders,
            [executor],
            options,
            clock,
            new UnusedPromoter(),
            grainKey: "00000000-0000-0000-0000-000000000000",
            grainTypeName: "FakeHost",
            deferredDispatch: _ => Task.CompletedTask);

        await host.DrainAsync();

        // EntryB never attempted: stop-at-head left it untouched behind the
        // backed-off head.
        await Verify(new { executor.Attempts, executor.Attempted, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    sealed class ThrowingInvokeHandlerExecutor : IOutboxEffectExecutor
    {
        public List<Guid> Attempted { get; } = [];
        public int Attempts => Attempted.Count;

        public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            Attempted.Add(entry.EntryId);
            throw new InvalidOperationException("simulated transient handler failure");
        }
    }

    sealed class UnusedPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(
            OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
            => throw new InvalidOperationException("No promotion expected at default MaxAttempts after one failure.");

        public OutboxEntry PromoteBlobMissing(
            EdictEventEnvelope envelope, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
            => throw new InvalidOperationException("No blob-missing promotion expected in this test.");
    }

    sealed class FakePersistentState : IPersistentState<GrainEnvelope<EdictUnit>>
    {
        public GrainEnvelope<EdictUnit> State { get; set; } = new();
        public string Etag => "";
        public bool RecordExists => true;

        public Task WriteStateAsync() => Task.CompletedTask;
        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
    }

    sealed class FakeReminderRegistrar : IReminderRegistrar
    {
        public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period) =>
            Task.CompletedTask;

        public Task UnregisterReminderAsync(string name) => Task.CompletedTask;
    }

    sealed class FakeStreamProvider : IStreamProvider
    {
        public static readonly FakeStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;

        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException("FakeStreamProvider has no streams.");
    }
}
