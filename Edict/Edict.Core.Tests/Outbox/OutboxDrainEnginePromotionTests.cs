using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;

using Microsoft.Extensions.Time.Testing;

using Orleans.Streams;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

// Promotion path of the OutboxDrainEngine under ADR 0022: when a head entry
// exhausts MaxAttempts the engine removes it from Pending and appends a new
// PublishEvent entry built by IDeadLetterPromoter, in the same one
// grain-state write — there is no DeadLetter slice. Driven via the same
// IOutboxHost fake + FakeTimeProvider pattern as OutboxDrainEngineTests so
// the assertion is a single Verify snapshot.
public sealed class OutboxDrainEnginePromotionTests
{
    static readonly Guid EntryA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid EntryB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly Guid PromotedId = new("dddddddd-0000-0000-0000-000000000099");
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    static OutboxEntry Entry(Guid id) => new()
    {
        EntryId = id,
        Kind = OutboxEffectKind.PublishEvent,
        Payload = [1, 2, 3],
    };

    static EdictOutboxOptions Options() => new()
    {
        MaxAttempts = 3,
        BaseDelay = TimeSpan.FromSeconds(2),
        JitterFraction = 0,
    };

    [Fact]
    public void DrainAsync_ShouldNotMutateAnyDeadLetterSlice_WhenMaxAttemptsExceeded()
    {
        // ADR 0022: the in-grain DeadLetter slot is gone — there is no slice
        // for the engine to mutate. The Outbox slice exposes Pending only.
        // A reflection assertion (rather than a behavioural one) is the
        // right shape: removing the slot is a *type-level* invariant, so the
        // regression guard is best expressed against the type.
        var sliceProps = typeof(OutboxSlice).GetProperties().Select(p => p.Name).ToArray();

        Assert.Contains(nameof(OutboxSlice.Pending), sliceProps);
        Assert.DoesNotContain("DeadLetter", sliceProps);
    }

    [Fact]
    public async Task DrainAsync_ShouldUseSingleStateWrite_WhenPromotingAndContinuingDrain()
    {
        // ADR 0022: the failing entry removal and the dead-letter PublishEvent
        // append commit together — one CommitAsync call covers both mutations,
        // so the engine cannot observe a state where the original effect is
        // gone but the dead-letter notification is absent (or vice versa). The
        // host.Log records the Pending contents at each commit, so the
        // promotion commit must show Pending=[PromotedId] in a single entry
        // (not two separate commits surrounding an intermediate state).
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA);
        var promoter = new FakePromoter(PromotedId);
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            GrainKey = "11111111-1111-1111-1111-111111111111",
            GrainTypeName = "Sample.OrderCommandHandler",
        };
        var engine = new OutboxDrainEngine([executor], clock, Options(), promoter);

        for (var pass = 0; pass < 3; pass++)
        {
            await engine.DrainAsync(host);
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { host.Log })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldPreserveFifo_WhenPromotedEntryAppendedAtTail()
    {
        // ADR 0022: the promoted entry is appended at the FIFO tail (not the
        // head), so an existing pending tail entry drains before the new
        // dead-letter publish — per-aggregate causal order is preserved.
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA);
        var promoter = new FakePromoter(PromotedId);
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
            GrainKey = "11111111-1111-1111-1111-111111111111",
            GrainTypeName = "Sample.OrderCommandHandler",
        };
        var engine = new OutboxDrainEngine([executor], clock, Options(), promoter);

        for (var pass = 0; pass < 3; pass++)
        {
            await engine.DrainAsync(host);
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldRemoveFailedEntryAndAppendPublishEvent_WhenMaxAttemptsExceeded()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA);
        var promoter = new FakePromoter(PromotedId);
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            GrainKey = "11111111-1111-1111-1111-111111111111",
            GrainTypeName = "Sample.OrderCommandHandler",
        };
        var engine = new OutboxDrainEngine([executor], clock, Options(), promoter);

        // Three failing passes exhaust MaxAttempts; the third triggers promotion.
        // Past the promotion, the Promoted entry (not poison) drains and acks,
        // so the final Outbox is empty.
        for (var pass = 0; pass < 3; pass++)
        {
            await engine.DrainAsync(host);
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { promoter.Calls, executor.Executed, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldStopAtPromotedEntry_WhenSubsequentEntryAlsoFails()
    {
        // ADR 0022: the promoted entry is just another Outbox PublishEvent
        // entry — the engine's existing stop-at-head + backoff semantics apply
        // unchanged. If the dead-letter publish itself fails, the engine
        // FailHeadWithBackoff's it (no second promotion in the same pass) and
        // stops at the head, leaving the Reminder to retry.
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA, PromotedId);
        var promoter = new FakePromoter(PromotedId);
        var host = new FakeOutboxHost
        {
            Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            GrainKey = "11111111-1111-1111-1111-111111111111",
            GrainTypeName = "Sample.OrderCommandHandler",
        };
        var engine = new OutboxDrainEngine([executor], clock, Options(), promoter);

        for (var pass = 0; pass < 3; pass++)
        {
            await engine.DrainAsync(host);
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { executor.Attempted, executor.Executed, host.Log, host.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    sealed class SelectiveExecutor : IOutboxEffectExecutor
    {
        readonly HashSet<Guid> _poison;

        public SelectiveExecutor(params Guid[] poison)
        {
            _poison = [.. poison];
        }

        public List<Guid> Executed { get; } = [];
        public List<Guid> Attempted { get; } = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(OutboxEntry entry, IOutboxHost host)
        {
            Attempted.Add(entry.EntryId);
            if (_poison.Contains(entry.EntryId))
            {
                throw new InvalidOperationException("poison entry");
            }

            Executed.Add(entry.EntryId);
            return Task.CompletedTask;
        }
    }

    sealed class FakePromoter(Guid promotedId) : IDeadLetterPromoter
    {
        public List<PromoteCall> Calls { get; } = [];

        public OutboxEntry Promote(
            OutboxEntry failed,
            Exception exception,
            string sourceGrainKey,
            string sourceGrainType,
            DateTimeOffset now)
        {
            Calls.Add(new PromoteCall(
                failed.EntryId,
                failed.AttemptCount,
                exception.GetType().FullName!,
                exception.Message,
                sourceGrainKey,
                sourceGrainType,
                now));

            return new OutboxEntry
            {
                EntryId = promotedId,
                Kind = OutboxEffectKind.PublishEvent,
                Payload = [9, 9, 9],
                TraceParent = failed.TraceParent,
                TraceState = failed.TraceState,
                AttemptCount = 0,
                NextAttemptUtc = now,
            };
        }
    }

    public sealed record PromoteCall(
        Guid FailedEntryId,
        int AttemptCount,
        string ExceptionType,
        string Reason,
        string SourceGrainKey,
        string SourceGrainType,
        DateTimeOffset Now);

    sealed class FakeOutboxHost : IOutboxHost
    {
        public List<string> Log { get; } = [];
        public OutboxSlice Outbox { get; set; } = new();
        public IStreamProvider StreamProvider => null!;
        public string GrainKey { get; set; } = "";
        public string GrainTypeName { get; set; } = "";

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

        public Task DispatchEventAsync(EdictEvent evt) =>
            throw new NotSupportedException("FakeOutboxHost does not route deferred dispatch.");
    }
}
