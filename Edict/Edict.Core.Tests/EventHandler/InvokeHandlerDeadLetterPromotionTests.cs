using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.EventHandler;

// ADR 0023 dead-letter promotion: a failed InvokeHandler entry exhausts its
// retries, the DeadLetterPromoter builds an EdictDeadLetterRaised carrying the
// originating event's type and id in SourceEventType + SourceEventId so
// operators can filter the dead-letter projection by event type without
// parsing payload bytes. Existing kinds (PublishEvent / SendCommand /
// UpsertRow) leave both fields null.
public sealed class InvokeHandlerDeadLetterPromotionTests
{
    static readonly Serializer Serializer = BuildSerializer();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Promote_ShouldPopulateSourceEventTypeAndId_ForInvokeHandlerFailure()
    {
        var evt = new OrderPlacedEvent(
            OrderId: new Guid("11111111-1111-1111-1111-111111111111"),
            Sku: "WIDGET")
        {
            EventId = new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OccurredAt = Now,
        };
        var failed = new OutboxEntry
        {
            EntryId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(evt),
            AttemptCount = 5,
            TraceParent = "00-0123456789abcdef0123456789abcdef-fedcba9876543210-01",
        };
        var promoter = new DeadLetterPromoter(Serializer, new ServiceCollection().BuildServiceProvider());

        var promoted = promoter.Promote(
            failed,
            new InvalidOperationException("SMTP gateway 503"),
            sourceGrainKey: "11111111-1111-1111-1111-111111111111",
            sourceGrainType: "Sample.OrderEmailHandler",
            now: Now);

        // The promoted entry is a new PublishEvent carrying the
        // EdictDeadLetterRaised — verify its deserialised shape so the
        // SourceEventType + SourceEventId are observable.
        var raised = Serializer.Deserialize<EdictEvent>(promoted.Payload);

        await Verify(raised).DontScrubGuids().DontScrubDateTimes();
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(InvokeHandlerDeadLetterPromotionTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }
}
