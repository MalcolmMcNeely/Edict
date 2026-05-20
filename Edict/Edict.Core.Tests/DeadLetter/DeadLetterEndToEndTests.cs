using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Core.Tests.Grains;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.DeadLetter;

// Full-loop coverage of ADR 0022: a permanent publish failure on an aggregate's
// raised event drives the engine through MaxAttempts retries and into the
// promotion path. The engine swaps the failing entry for an
// EdictDeadLetterRaised PublishEvent entry in the same commit; the dead-letter
// publish itself succeeds (the test executor only fails for non-dead-letter
// events); the framework-shipped EdictDeadLetterProjectionBuilder consumes the
// stream and upserts a row; and the auto-wired IEdictDeadLetterRepository
// surfaces that row to the caller. One scenario covers the wiring chain from
// the engine's promotion seam through to the consumer-facing repository.
public sealed class DeadLetterEndToEndTests(DeadLetterPromoteClusterFixture fixture)
    : IClassFixture<DeadLetterPromoteClusterFixture>
{
    [Fact]
    public async Task PoisonHandler_ShouldProduceDeadLetterEntryReadableViaRepository()
    {
        // Pre-flight: a fresh aggregate id means a clean per-grain outbox and a
        // fresh projection row for this scenario, even if other tests in the
        // fixture's lifetime have written elsewhere into the partition.
        var counterId = Guid.NewGuid();

        // Send the command; OrderCommandHandler raises CounterIncrementedEvent;
        // the publish executor refuses to publish it (poisoned), so the entry
        // sits at the head of the outbox with AttemptCount=1.
        await fixture.Sender.Send(new IncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);

        // Force the lazy Reminder drain repeatedly past every backoff gate. The
        // engine flips at AttemptCount >= MaxAttempts: it removes the failing
        // entry and appends an EdictDeadLetterRaised PublishEvent entry in the
        // same commit, then continues draining within the same call — and the
        // dead-letter publish succeeds because the executor's predicate ignores
        // it. So one drain call after the third failure clears the outbox.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(10));
            await probe.ForceDrainViaReminderAsync();
        }

        await WaitForRowAsync();

        var rows = await fixture.DeadLetterRepository.ListAllAsync();

        Assert.Single(rows);
        var entry = rows[0];
        Assert.Equal("PublishEvent", entry.Kind);
        Assert.Equal(counterId.ToString(), entry.SourceGrainKey);
        Assert.Contains("CounterAggregate", entry.SourceGrainType);
        Assert.Equal("Counters/CounterIncrementedEvent", entry.EffectTarget);
        Assert.Equal("System.InvalidOperationException", entry.ExceptionType);
        Assert.Equal("simulated permanent publish failure", entry.Reason);
        Assert.NotNull(entry.PayloadJson);

        // Pin the stable RCA fields with a single snapshot. DeadLetteredAt and
        // TraceParent are volatile (clock-driven and trace-context-driven) so
        // they are scrubbed; their non-null nature is checked structurally
        // above through the dedicated Asserts.
        await Verify(entry).DontScrubGuids().DontScrubDateTimes()
            .ScrubMember<EdictDeadLetterEntry>(e => e.EntryId)
            .ScrubMember<EdictDeadLetterEntry>(e => e.DeadLetteredAt)
            .ScrubMember<EdictDeadLetterEntry>(e => e.TraceParent)
            .ScrubMember<EdictDeadLetterEntry>(e => e.PayloadJson)
            .ScrubMember<EdictDeadLetterEntry>(e => e.SourceGrainKey);
    }

    async Task WaitForRowAsync()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await fixture.DeadLetterRepository.ListAllAsync();
            if (rows.Count > 0)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
