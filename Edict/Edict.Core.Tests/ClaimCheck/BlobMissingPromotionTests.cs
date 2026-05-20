using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// Engine-path dead-letter promotion shape for a missing claim-check blob
/// (ADR 0026 fold of ADR 0024 slice 3): the receiver-side stream observer
/// stages an <see cref="OutboxEffectKind.InvokeHandler"/> entry whose payload
/// is a serialised pointer-bearing <see cref="EdictEventEnvelope"/>; the
/// engine's <see cref="OutboxHost{TPayload}"/> drains that entry, fetches
/// (and fails) via <see cref="ClaimCheckUnwrap"/>, runs the standard per-entry
/// backoff loop, and on <c>MaxAttempts</c> exhaustion routes
/// <see cref="DeadLetterPromoter.Promote"/> through the
/// <see cref="DeadLetterPromotion.BuildForBlobMissing"/> mapping. One Verify
/// snapshot pins the produced shape so a future field rename surfaces in CI.
/// </summary>
public sealed class BlobMissingPromotionTests
{
    static readonly Serializer Serializer = BuildSerializer();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Promote_ShouldProduceDeadLetterEntry_WithBlobMissingFailureKind_WhenInvokeHandlerEntryWrapsPointerEnvelope()
    {
        // A receiver-side staged InvokeHandler entry. The payload is the
        // pointer-bearing envelope itself (ADR 0026); the engine's failing
        // unwrap surfaces as KeyNotFoundException from the executor, and the
        // promoter must route through the BlobMissing branch.
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: "edict-claim-check/abc123")
        {
            EventId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OccurredAt = Now,
            TraceId = "0123456789abcdef0123456789abcdef",
            SpanId = "fedcba9876543210",
            InnerEventStreamName = "Orders",
            InnerEventRouteKey = new Guid("11111111-1111-1111-1111-111111111111"),
        };
        var entry = new OutboxEntry
        {
            EntryId = new Guid("99999999-9999-9999-9999-999999999999"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
            TraceParent = "00-0123456789abcdef0123456789abcdef-fedcba9876543210-01",
            TraceState = null,
            AttemptCount = 3,
            NextAttemptUtc = Now,
        };

        var promoter = new DeadLetterPromoter(Serializer, new ServiceCollection().BuildServiceProvider());
        var promoted = promoter.Promote(
            entry,
            new KeyNotFoundException("Claim-check blob 'edict-claim-check/abc123' was not found."),
            sourceGrainKey: "11111111-1111-1111-1111-111111111111",
            sourceGrainType: "Sample.OrderEmailHandler",
            now: Now);

        // The promoted entry is a new PublishEvent carrying the synthetic
        // EdictDeadLetterRaised — verify its deserialised shape so the
        // FailureKind + ClaimCheckKey are observable.
        var raised = Serializer.Deserialize<EdictEvent>(promoted.Payload);

        await Verify(raised).DontScrubGuids().DontScrubDateTimes();
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
