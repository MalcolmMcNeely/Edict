using Edict.Contracts;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// Host drain logic against a fake <see cref="IPersistentState{T}"/>, fake
/// <see cref="IReminderRegistrar"/> and a virtual clock — no cluster, no
/// backend (ADR 0016). Fixed Guids/time so the Verify snapshot is the
/// assertion. The shared <c>Log</c> orders writes and reminder calls so the
/// snapshot shape matches the pre-composition <c>OutboxDrainEngineTests</c>
/// (same scope, same coverage, no behavioural change). Drain-on-activation,
/// deferred-dispatch callback and trace-context capture/restore are new
/// surface specific to the composed host.
/// </summary>
public sealed class OutboxHostTests
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

    static EdictOutboxOptions PromotionOptions() => new()
    {
        MaxAttempts = 3,
        BaseDelay = TimeSpan.FromSeconds(2),
        JitterFraction = 0,
    };

    static OutboxHost<EdictUnit> Host(
        FakePersistentState state,
        FakeReminderRegistrar reminders,
        IOutboxEffectExecutor executor,
        FakeTimeProvider clock,
        EdictOutboxOptions? options = null,
        IDeadLetterPromoter? promoter = null,
        Func<EdictEvent, Task>? deferredDispatch = null,
        ClaimCheckPolicy? claimCheckPolicy = null) =>
        new(
            state,
            FakeStreamProvider.Instance,
            reminders,
            [executor],
            options ?? new EdictOutboxOptions(),
            clock,
            promoter ?? new UnusedPromoter(),
            grainKey: "00000000-0000-0000-0000-000000000000",
            grainTypeName: "FakeHost",
            deferredDispatch,
            claimCheckPolicy);

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(OutboxHostTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    // ---- Migrated from OutboxDrainEngineTests ---------------------------------

    [Fact]
    public async Task DrainAsync_ShouldExecuteAckAndUnregister_WhenAllEffectsSucceed()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock);
        await host.DrainAsync();

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldFailBackoffStopAndRegister_WhenEffectThrows()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new ThrowingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);

        // Must not surface: the post-commit failure stays inside the host.
        var host = Host(state, reminders, executor, clock);
        await host.DrainAsync();

        await Verify(new { executor.Attempts, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldStopAtHead_WhenHeadBackoffGated()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var gated = Entry(EntryA) with { AttemptCount = 1, NextAttemptUtc = Now.AddMinutes(1) };
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(gated).Enqueue(Entry(EntryB)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock);
        await host.DrainAsync();

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldUnregister_WhenNothingPending()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock);
        await host.DrainAsync();

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task EnqueueAndDrainAsync_ShouldCommitOnceThenDrainFifo()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock);
        await host.EnqueueAndDrainAsync([Entry(EntryA), Entry(EntryB)]);

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
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
        var log = new List<string>();
        var gated = Entry(EntryA) with { AttemptCount = 1, NextAttemptUtc = Now.AddMinutes(5) };
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(gated),
            },
        };
        var reminders = new FakeReminderRegistrar(log);
        var host = Host(state, reminders, executor, clock);

        await host.DrainAsync();          // gated: skipped, not executed
        var skippedExecuted = executor.Executed.Count;

        clock.Advance(TimeSpan.FromMinutes(6));  // past NextAttemptUtc
        await host.DrainAsync();          // now due: published

        await Verify(new { skippedExecuted, executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    // ---- Migrated from OutboxDrainEnginePromotionTests ------------------------

    [Fact]
    public void DrainAsync_ShouldNotMutateAnyDeadLetterSlice_WhenMaxAttemptsExceeded()
    {
        // ADR 0022: the in-grain DeadLetter slot is gone — there is no slice
        // for the host to mutate. The Outbox slice exposes Pending only.
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
        // append commit together — one WriteStateAsync call covers both
        // mutations, so the host cannot observe a state where the original
        // effect is gone but the dead-letter notification is absent (or vice
        // versa). The Log records the Pending contents at each commit, so the
        // promotion commit must show Pending=[PromotedId] in a single entry
        // (not two separate commits surrounding an intermediate state).
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA);
        var promoter = new FakePromoter(PromotedId);
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);
        var host = new OutboxHost<EdictUnit>(
            state,
            FakeStreamProvider.Instance,
            reminders,
            [executor],
            PromotionOptions(),
            clock,
            promoter,
            grainKey: "11111111-1111-1111-1111-111111111111",
            grainTypeName: "Sample.OrderCommandHandler");

        for (var pass = 0; pass < 3; pass++)
        {
            await host.DrainAsync();
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { Log = log })
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
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);
        var host = new OutboxHost<EdictUnit>(
            state,
            FakeStreamProvider.Instance,
            reminders,
            [executor],
            PromotionOptions(),
            clock,
            promoter,
            grainKey: "11111111-1111-1111-1111-111111111111",
            grainTypeName: "Sample.OrderCommandHandler");

        for (var pass = 0; pass < 3; pass++)
        {
            await host.DrainAsync();
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldRemoveFailedEntryAndAppendPublishEvent_WhenMaxAttemptsExceeded()
    {
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA);
        var promoter = new FakePromoter(PromotedId);
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);
        var host = new OutboxHost<EdictUnit>(
            state,
            FakeStreamProvider.Instance,
            reminders,
            [executor],
            PromotionOptions(),
            clock,
            promoter,
            grainKey: "11111111-1111-1111-1111-111111111111",
            grainTypeName: "Sample.OrderCommandHandler");

        // Three failing passes exhaust MaxAttempts; the third triggers
        // promotion. Past the promotion, the Promoted entry (not poison)
        // drains and acks, so the final Outbox is empty.
        for (var pass = 0; pass < 3; pass++)
        {
            await host.DrainAsync();
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { promoter.Calls, executor.Executed, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldStopAtPromotedEntry_WhenSubsequentEntryAlsoFails()
    {
        // ADR 0022: the promoted entry is just another Outbox PublishEvent
        // entry — the host's existing stop-at-head + backoff semantics apply
        // unchanged. If the dead-letter publish itself fails, the host
        // FailHeadWithBackoff's it (no second promotion in the same pass) and
        // stops at the head, leaving the Reminder to retry.
        var clock = new FakeTimeProvider(Now);
        var executor = new SelectiveExecutor(EntryA, PromotedId);
        var promoter = new FakePromoter(PromotedId);
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);
        var host = new OutboxHost<EdictUnit>(
            state,
            FakeStreamProvider.Instance,
            reminders,
            [executor],
            PromotionOptions(),
            clock,
            promoter,
            grainKey: "11111111-1111-1111-1111-111111111111",
            grainTypeName: "Sample.OrderCommandHandler");

        for (var pass = 0; pass < 3; pass++)
        {
            await host.DrainAsync();
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        await Verify(new { executor.Attempted, executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    // ---- ClaimCheckPolicy wiring (ADR 0024, slice 2) --------------------------

    [Fact]
    public async Task EnqueueRaisedEventsAndDrainAsync_ShouldParallelisePolicyPuts_WhenMultipleOversizedEventsBuffered()
    {
        // ADR 0024 / issue #72: a Handle that raises N oversized events at the
        // commit boundary pays one I/O round trip, not N. The policy invocations
        // run inside Task.WhenAll, so a store whose PutAsync blocks until N
        // concurrent in-flight calls have arrived completes.
        var serializer = BuildSerializer();
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);
        var store = new ConcurrencyGate(expectedConcurrency: 3);
        var policy = new ClaimCheckPolicy(serializer, thresholdBytes: 1, store);

        var host = Host(state, reminders, executor, clock, claimCheckPolicy: policy);

        var routeKey = new Guid("11111111-2222-3333-4444-555555555555");
        var events = new EdictEvent[]
        {
            new OrderPlacedEvent(routeKey, "SKU-A"),
            new OrderPlacedEvent(routeKey, "SKU-B"),
            new OrderPlacedEvent(routeKey, "SKU-C"),
        };

        await host.EnqueueRaisedEventsAndDrainAsync(events, traceParent: null, traceState: null);

        Assert.Equal(3, store.MaxObservedConcurrency);
        Assert.Equal(3, executor.Executed.Count);
    }

    [Fact]
    public async Task EnqueueRaisedEventsAndDrainAsync_ShouldNoOp_WhenEventListEmpty()
    {
        // No events raised means no commit and no drain — the host must not
        // write state nor touch the reminder subsystem.
        var serializer = BuildSerializer();
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);
        var policy = new ClaimCheckPolicy(serializer, thresholdBytes: int.MaxValue, store: null);

        var host = Host(state, reminders, executor, clock, claimCheckPolicy: policy);
        await host.EnqueueRaisedEventsAndDrainAsync([], traceParent: null, traceState: null);

        Assert.Empty(log);
    }

    [Fact]
    public async Task EnqueueRaisedEventsAndDrainAsync_ShouldStageInnerEventBytes_WhenUnderThreshold()
    {
        // Conditional-wrap (slice 2): small events ride the entry payload as
        // the serialised inner event itself, not as a wrapping envelope. The
        // executor sees the raw inner-event bytes the policy produced.
        var serializer = BuildSerializer();
        var clock = new FakeTimeProvider(Now);
        var executor = new PayloadCapturingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);
        var policy = new ClaimCheckPolicy(serializer, thresholdBytes: int.MaxValue, store: null);

        var host = Host(state, reminders, executor, clock, claimCheckPolicy: policy);

        var evt = new OrderPlacedEvent(new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "SKU");
        var expected = serializer.SerializeToArray<EdictEvent>(evt);

        await host.EnqueueRaisedEventsAndDrainAsync([evt], traceParent: null, traceState: null);

        Assert.Single(executor.SeenPayloads);
        Assert.Equal(expected, executor.SeenPayloads[0]);
    }

    // ---- New surface specific to the composed host ----------------------------

    [Fact]
    public async Task OnActivateAsync_ShouldDrain_WhenOutboxIsNonEmpty()
    {
        // Composition equivalent of EdictDurableConsumerBase's pre-refactor
        // drain-on-activation: the host is told to activate, sees a non-empty
        // Outbox, runs the executor on every pending entry, and unregisters
        // the (never-registered) Reminder because nothing is left.
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)).Enqueue(Entry(EntryB)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock);
        await host.OnActivateAsync();

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task OnActivateAsync_ShouldSkipDrain_WhenOutboxIsEmpty()
    {
        // Steady-state activation: no pending entries, the drain path is
        // bypassed entirely and the reminder subsystem is not touched.
        var clock = new FakeTimeProvider(Now);
        var executor = new RecordingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock);
        await host.OnActivateAsync();

        await Verify(new { executor.Executed, Log = log, host.State.Outbox })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldInvokeDeferredDispatchCallback_WhenInvokeHandlerEntryDrains()
    {
        // ADR 0023: an InvokeHandler entry's executor routes the deserialised
        // EdictEvent back into the host's deferred-dispatch callback so the
        // consumer's Handle(TEvent) runs off the stream-callback path with
        // retry/backoff/dead-letter wrapping. The callback fires exactly once
        // per successful drain.
        var clock = new FakeTimeProvider(Now);
        var dispatched = new List<EdictEvent>();
        var executor = new InvokeCapturingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(new OutboxEntry
                {
                    EntryId = EntryA,
                    Kind = OutboxEffectKind.InvokeHandler,
                    Payload = [9, 9, 9],
                }),
            },
        };
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(
            state, reminders, executor, clock,
            deferredDispatch: evt => { dispatched.Add(evt); return Task.CompletedTask; });

        await host.DrainAsync();

        await Verify(new { dispatched = dispatched.Count, executor.SeenCallbacks })
            .DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public async Task DrainAsync_ShouldPassNullDispatchCallback_WhenHostHasNoneWired()
    {
        // The command-handler shell never wires a deferred-dispatch callback
        // (Commands are a direct grain call, not an event route). The host
        // hands the executor a null callback in that case; an executor that
        // only consumes the stream provider (PublishEventExecutor,
        // SendCommandExecutor, UpsertRowExecutor) does not care.
        var clock = new FakeTimeProvider(Now);
        var executor = new CallbackProbeExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log)
        {
            State = new GrainEnvelope<EdictUnit>
            {
                Outbox = new OutboxSlice().Enqueue(Entry(EntryA)),
            },
        };
        var reminders = new FakeReminderRegistrar(log);

        var host = Host(state, reminders, executor, clock); // no deferredDispatch
        await host.DrainAsync();

        Assert.True(executor.SawNullCallback);
    }

    [Fact]
    public async Task EnqueueAndDrainAsync_ShouldCarryTraceContext_OnStagedEntries()
    {
        // ADR 0003: trace ids the *caller* captured are carried on staged
        // entries verbatim; the executor restores them as the publish span's
        // parent. The host neither captures nor rewrites the entry's
        // TraceParent/TraceState — both are the caller's responsibility — but
        // it must hand them through to the executor unchanged.
        var clock = new FakeTimeProvider(Now);
        var executor = new TraceCapturingExecutor();
        var log = new List<string>();
        var state = new FakePersistentState(log);
        var reminders = new FakeReminderRegistrar(log);

        var entry = Entry(EntryA) with
        {
            TraceParent = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-01",
            TraceState = "rojo=00f067aa0ba902b7",
        };

        var host = Host(state, reminders, executor, clock);
        await host.EnqueueAndDrainAsync([entry]);

        await Verify(new { executor.SeenTraceParent, executor.SeenTraceState })
            .DontScrubGuids().DontScrubDateTimes();
    }

    // ---- Fakes ----------------------------------------------------------------

    sealed class RecordingExecutor : IOutboxEffectExecutor
    {
        public List<Guid> Executed { get; } = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            Executed.Add(entry.EntryId);
            return Task.CompletedTask;
        }
    }

    sealed class ThrowingExecutor : IOutboxEffectExecutor
    {
        public int Attempts { get; private set; }
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            Attempts++;
            throw new InvalidOperationException("downstream unavailable");
        }
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

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
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

    sealed class InvokeCapturingExecutor : IOutboxEffectExecutor
    {
        public int SeenCallbacks { get; private set; }
        public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

        public async Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            if (deferredDispatch is not null)
            {
                SeenCallbacks++;
                await deferredDispatch(new OrderPlacedEvent(
                    OrderId: new Guid("44444444-4444-4444-4444-444444444444"),
                    Sku: "DEFERRED-DISPATCH"));
            }
        }
    }

    sealed class CallbackProbeExecutor : IOutboxEffectExecutor
    {
        public bool SawNullCallback { get; private set; }
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            SawNullCallback = deferredDispatch is null;
            return Task.CompletedTask;
        }
    }

    sealed class PayloadCapturingExecutor : IOutboxEffectExecutor
    {
        public List<byte[]> SeenPayloads { get; } = [];
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            SeenPayloads.Add(entry.Payload);
            return Task.CompletedTask;
        }
    }

    sealed class ConcurrencyGate(int expectedConcurrency) : IEdictClaimCheckStore
    {
        readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _inFlight;
        int _max;

        public int MaxObservedConcurrency => _max;

        public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            var seen = Interlocked.Increment(ref _inFlight);
            // Track the high-water mark of simultaneous in-flight calls so the
            // test can assert N concurrent puts on N oversized events.
            while (true)
            {
                var prior = _max;
                if (seen <= prior || Interlocked.CompareExchange(ref _max, seen, prior) == prior)
                {
                    break;
                }
            }

            if (seen >= expectedConcurrency)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task;
            Interlocked.Decrement(ref _inFlight);
            return $"blob-{Guid.NewGuid():N}";
        }

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken ct) =>
            throw new NotSupportedException("publisher-side tests never fetch");
    }

    sealed class TraceCapturingExecutor : IOutboxEffectExecutor
    {
        public string? SeenTraceParent { get; private set; }
        public string? SeenTraceState { get; private set; }
        public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

        public Task ExecuteAsync(
            OutboxEntry entry, IStreamProvider streamProvider, Func<EdictEvent, Task>? deferredDispatch)
        {
            SeenTraceParent = entry.TraceParent;
            SeenTraceState = entry.TraceState;
            return Task.CompletedTask;
        }
    }

    sealed class UnusedPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now)
            => throw new InvalidOperationException("No promotion expected in this test.");
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

    sealed class FakePersistentState(List<string> log) : IPersistentState<GrainEnvelope<EdictUnit>>
    {
        public GrainEnvelope<EdictUnit> State { get; set; } = new();
        public string Etag => "";
        public bool RecordExists => true;

        public Task WriteStateAsync()
        {
            log.Add($"commit[{string.Join(",", State.Outbox.Pending.Select(p => p.EntryId))}]");
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task ClearStateAsync() => Task.CompletedTask;
    }

    sealed class FakeReminderRegistrar(List<string> log) : IReminderRegistrar
    {
        public Task RegisterOrUpdateReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
        {
            log.Add("register");
            return Task.CompletedTask;
        }

        public Task UnregisterReminderAsync(string name)
        {
            log.Add("unregister");
            return Task.CompletedTask;
        }
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
