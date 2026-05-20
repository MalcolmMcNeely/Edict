using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using Microsoft.Extensions.Time.Testing;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.EventHandler;

// FIFO stop-at-head for InvokeHandler entries per host grain (ADR 0023): a
// transient failure on the head InvokeHandler entry blocks subsequent entries
// from running until backoff retries succeed. The engine is kind-agnostic for
// FIFO, so this exercises the same stop-at-head behaviour proven for
// PublishEvent in OutboxDrainEngineTests against the new InvokeHandler kind.
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
        var host = new FakeHost
        {
            Outbox = new OutboxSlice()
                .Enqueue(InvokeHandlerEntry(EntryA))
                .Enqueue(InvokeHandlerEntry(EntryB)),
        };
        var options = new EdictOutboxOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromSeconds(2),
            JitterFraction = 0,
        };
        var engine = new OutboxDrainEngine([executor], clock, options, new UnusedPromoter());

        await engine.DrainAsync(host);

        // EntryB never attempted: stop-at-head left it untouched behind the
        // backed-off head.
        await Verify(new { executor.Attempts, executor.Attempted, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    sealed class ThrowingInvokeHandlerExecutor : IOutboxEffectExecutor
    {
        public List<Guid> Attempted { get; } = [];
        public int Attempts => Attempted.Count;

        public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

        public Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
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
    }

    sealed class FakeHost : IOutboxHost
    {
        public List<string> Log { get; } = [];
        public OutboxSlice Outbox { get; set; } = new();
        public Orleans.Streams.IStreamProvider StreamProvider => null!;
        public string GrainKey => "00000000-0000-0000-0000-000000000000";
        public string GrainTypeName => "FakeHost";

        public Task CommitAsync() => Task.CompletedTask;
        public Task RegisterDrainReminderAsync() => Task.CompletedTask;
        public Task UnregisterDrainReminderAsync() => Task.CompletedTask;
        public Task DispatchEventAsync(EdictEvent evt) =>
            throw new NotSupportedException("FakeHost does not route deferred dispatch.");
    }
}
