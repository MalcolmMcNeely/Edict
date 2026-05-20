using Edict.Contracts.DeadLetter;
using Edict.Core.DeadLetter;
using Edict.Core.Tests.Grains;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.ClaimCheck;

// ADR 0024 slice 4 — full-loop coverage. A raised event commits as a
// pointer-bearing EdictEventEnvelope on the Outbox (threshold = 1); the
// poisoned publish executor refuses to publish; after MaxAttempts the engine
// promotes via DeadLetterPromoter, which detects the envelope and emits an
// EdictDeadLetterRaised with ClaimCheckKey populated and PayloadJson null.
// The framework-shipped EdictDeadLetterProjectionBuilder upserts the row;
// IEdictDeadLetterRepository surfaces it. The single Verify snapshot pins the
// forensic shape an operator sees.
public sealed class OversizedEventDeadLetterEndToEndTests(OversizedEventDeadLetterClusterFixture fixture)
    : IClassFixture<OversizedEventDeadLetterClusterFixture>
{
    [Fact]
    public async Task OversizedEventPermanentFailure_ShouldLandInDeadLetterWithClaimCheckKeyAndNullPayloadJson()
    {
        var counterId = Guid.NewGuid();

        await fixture.Sender.Send(new IncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);

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
        Assert.Equal($"Counters/{counterId:D}", entry.EffectTarget);
        Assert.Equal("System.InvalidOperationException", entry.ExceptionType);
        Assert.Equal("simulated permanent publish failure", entry.Reason);
        Assert.Null(entry.PayloadJson);
        Assert.NotNull(entry.ClaimCheckKey);
        Assert.StartsWith("edict-claim-check/", entry.ClaimCheckKey);
        Assert.Equal(EdictDeadLetterFailureKind.EffectFailure, entry.FailureKind);

        await Verify(entry).DontScrubGuids().DontScrubDateTimes()
            .ScrubMember<EdictDeadLetterEntry>(e => e.EntryId)
            .ScrubMember<EdictDeadLetterEntry>(e => e.DeadLetteredAt)
            .ScrubMember<EdictDeadLetterEntry>(e => e.TraceParent)
            .ScrubMember<EdictDeadLetterEntry>(e => e.SourceGrainKey)
            .ScrubMember<EdictDeadLetterEntry>(e => e.EffectTarget)
            .ScrubMember<EdictDeadLetterEntry>(e => e.ClaimCheckKey);
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
