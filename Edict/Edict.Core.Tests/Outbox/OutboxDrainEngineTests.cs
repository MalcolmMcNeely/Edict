using Edict.Contracts.Configuration;
using Edict.Core.Outbox;

using Microsoft.Extensions.Time.Testing;

using Orleans.Streams;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

// Engine drain logic against a fake host + virtual clock — no cluster, no
// backend (ADR 0016). Fixed Guids/time so the Verify snapshot is the
// assertion. Proves: success → AckHead + Unregister; post-commit failure →
// FailHeadWithBackoff, stop-at-head, Register, no throw; backoff-gated head
// stops the drain; empty outbox reconciles to Unregister.
public sealed class OutboxDrainEngineTests
{
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    static OutboxEntry Entry(Guid id) => new()
    {
        EntryId = id,
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [1, 2, 3],
    };

    static readonly EdictOutboxOptions Options = new();

    static OutboxDrainEngine Engine(IOutboxEffectExecutor executor, FakeTimeProvider clock) =>
        new([executor], clock, Options);

    [Fact]
    public async Task DrainAsync_ShouldExecuteAckAndUnregister_WhenAllEffectsSucceed()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
        };

        await Engine(executor, clock).DrainAsync(host);

        await Verify(new { executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldFailBackoffStopAndRegister_WhenEffectThrows()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new ThrowingExecutor();
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
        };

        // Must not surface: the post-commit failure stays inside the engine.
        await Engine(executor, clock).DrainAsync(host);

        await Verify(new { executor.Attempts, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldStopAtHead_WhenHeadBackoffGated()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var gated = Entry(EntryA) with { AttemptCount = 1, NextAttemptUtc = Now.AddMinutes(1) };
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(gated).Enqueue(Entry(EntryB)),
        };

        await Engine(executor, clock).DrainAsync(host);

        await Verify(new { executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldUnregister_WhenNothingPending()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var host = new FakeOutboxHost { Outbox = new OutboxSlice() };

        await Engine(executor, clock).DrainAsync(host);

        await Verify(new { executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task EnqueueAndDrainAsync_ShouldCommitOnceThenDrainFifo()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var host = new FakeOutboxHost();

        await Engine(executor, clock)
            .EnqueueAndDrainAsync(host, [Entry(EntryA), Entry(EntryB)]);

        await Verify(new { executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    // ADR 0019: a permanently failing head is retried with backoff, then at
    // MaxAttempts moves Outbox→DeadLetter in the same one commit and the drain
    // CONTINUES — the tail (EntryB) is no longer blocked (self-healing).
    [Fact]
    public async Task DrainAsync_ShouldDeadLetterPoisonHeadAndFreeTail_WhenMaxAttemptsExhausted()
    {
        var clock = new FakeTimeProvider(Now);
        var options = new EdictOutboxOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromSeconds(2),
            JitterFraction = 0,
        };
        var executor = new SelectiveExecutor(poison: EntryA);
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
        };
        var engine = new OutboxDrainEngine([executor], clock, options);

        // Each pass: poison head throws, backoff-gated; advance past the gate
        // and drain again. The 3rd failure exhausts MaxAttempts → dead-letter.
        for (var pass = 0; pass < 3; pass++)
        {
            await engine.DrainAsync(host);
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    // ADR 0019: the same one Reminder gates retries on NextAttemptUtc — an
    // entry not yet due is skipped (not executed); once the virtual clock
    // advances past its gate the very next drain publishes it.
    [Fact]
    public async Task DrainAsync_ShouldSkipGatedEntryThenPublish_WhenClockAdvancesPastNextAttempt()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var gated = Entry(EntryA) with { AttemptCount = 1, NextAttemptUtc = Now.AddMinutes(5) };
        var host = new FakeOutboxHost { Outbox = new OutboxSlice().Enqueue(gated) };
        var engine = new OutboxDrainEngine([executor], clock, Options);

        await engine.DrainAsync(host);          // gated: skipped, not executed
        var skippedExecuted = executor.Executed.Count;

        clock.Advance(TimeSpan.FromMinutes(6));  // past NextAttemptUtc
        await engine.DrainAsync(host);          // now due: published

        await Verify(new { skippedExecuted, executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    sealed class SelectiveExecutor(Guid poison) : IOutboxEffectExecutor
    {
        public List<Guid> Executed { get; } = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider)
        {
            if (entry.EntryId == poison)
            {
                throw new InvalidOperationException("poison entry");
            }

            Executed.Add(entry.EntryId);
            return Task.CompletedTask;
        }
    }

    sealed class RecordingExecutor : IOutboxEffectExecutor
    {
        public List<Guid> Executed { get; } = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider)
        {
            Executed.Add(entry.EntryId);
            return Task.CompletedTask;
        }
    }

    sealed class ThrowingExecutor : IOutboxEffectExecutor
    {
        public int Attempts { get; private set; }
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(OutboxEntry entry, IStreamProvider streamProvider)
        {
            Attempts++;
            throw new InvalidOperationException("downstream unavailable");
        }
    }

    sealed class FakeOutboxHost : IOutboxHost
    {
        public List<string> Log { get; } = [];
        public OutboxSlice Outbox { get; set; } = new();
        public IStreamProvider StreamProvider => null!; // never touched by fake executors

        public Task CommitAsync()
        {
            Log.Add($"commit[{string.Join(",", Outbox.Pending.Select(p => p.EntryId))}]");
            return Task.CompletedTask;
        }

        public Task RegisterDrainReminderAsync()
        {
            Log.Add("register");
            return Task.CompletedTask;
        }

        public Task UnregisterDrainReminderAsync()
        {
            Log.Add("unregister");
            return Task.CompletedTask;
        }
    }
}
