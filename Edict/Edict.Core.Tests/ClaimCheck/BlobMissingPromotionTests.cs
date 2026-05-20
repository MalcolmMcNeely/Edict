using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Receiver-side dead-letter promotion shape (ADR 0024, slice 3): when the
/// stream-observer machinery exhausts <see cref="Edict.Contracts.Configuration.EdictOutboxOptions.MaxAttempts"/>
/// retries fetching a claim-check blob, it asks the
/// <see cref="IDeadLetterPromoter"/> to build a synthetic
/// <c>EdictDeadLetterRaised</c> with <see cref="EdictDeadLetterFailureKind.BlobMissing"/>
/// and the missing key, route key, and consumer grain identity. One Verify
/// snapshot pins the produced shape so a future field rename surfaces in CI.
/// </summary>
public sealed class BlobMissingPromotionTests
{
    static readonly Serializer Serializer = BuildSerializer();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PromoteBlobMissing_ShouldProduceDeadLetterEntry_WithBlobMissingFailureKindAndKey()
    {
        // The envelope's identity (EventId, route key) carries the framework's
        // bookkeeping; the receiver-side promotion path translates it into a
        // synthetic dead-letter outcome so the operator's forensic surface
        // names the inbound event that could not be materialised.
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "edict-claim-check/abc123")
        {
            EventId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OccurredAt = Now,
            TraceId = "0123456789abcdef0123456789abcdef",
            SpanId = "fedcba9876543210",
            InnerEventStreamName = "Orders",
            InnerEventRouteKey = new Guid("11111111-1111-1111-1111-111111111111"),
        };

        var promoter = new DeadLetterPromoter(Serializer, new ServiceCollection().BuildServiceProvider());

        var promoted = promoter.PromoteBlobMissing(
            envelope,
            sourceGrainKey: "11111111-1111-1111-1111-111111111111",
            sourceGrainType: "Sample.OrderEmailHandler",
            now: Now);

        // The promoted entry is a new PublishEvent carrying the synthetic
        // EdictDeadLetterRaised — verify its deserialised shape so the
        // FailureKind + ClaimCheckKey are observable.
        var raised = Serializer.Deserialize<EdictEvent>(promoted.Payload);

        // The synthetic dead-letter entry has no source OutboxEntry, so EntryId
        // is freshly minted in BuildForBlobMissing; scrub it so the snapshot
        // pins only the deterministic shape.
        await Verify(raised).DontScrubGuids().DontScrubDateTimes()
            .ScrubMember<EdictDeadLetterRaised>(e => e.EntryId);
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(BlobMissingPromotionTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
