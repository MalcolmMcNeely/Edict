using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Edict.Core.Tests.DeadLetter;

// A dead-letter notification originates at the Promote choke point — it is not
// a re-publish of a source event, so it gets its own fresh identity there.
// Reusing the source event's id would collide in the singleton dead-letter
// projection's dedup ring (two failures of one source event = two rows). Since
// the drain no longer mints an id, Promote must stamp it or the forensic event
// publishes with Guid.Empty and the projection drops every dead-letter but the
// first.
public sealed class DeadLetterPromoterEventIdTests
{
    static readonly IServiceProvider SerializerProvider = BuildSerializerProvider();
    static readonly Serializer Serializer = SerializerProvider.GetRequiredService<Serializer>();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Promote_ShouldStampNonEmptyEventId_OnTheForensicNotification()
    {
        var promoter = BuildPromoter();
        var entry = PublishEntry(new OrderPlacedEvent(Guid.NewGuid(), "SKU-1"));

        var promoted = promoter.Promote(
            entry, new InvalidOperationException("downstream"),
            sourceGrainKey: "grain-key", sourceGrainType: "Sample.OrderCommandHandler", now: Now);

        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.NotEqual(Guid.Empty, raised.EventId);
    }

    [Fact]
    public void Promote_ShouldStampDistinctEventIds_ForTwoFailuresOfTheSameSourceEvent()
    {
        var promoter = BuildPromoter();
        var sourceEvent = new OrderPlacedEvent(Guid.NewGuid(), "SKU-1");

        var first = promoter.Promote(
            PublishEntry(sourceEvent), new InvalidOperationException("downstream"),
            "grain-key", "Sample.OrderCommandHandler", Now);
        var second = promoter.Promote(
            PublishEntry(sourceEvent), new InvalidOperationException("downstream"),
            "grain-key", "Sample.OrderCommandHandler", Now);

        var firstId = Serializer.Deserialize<EdictEvent>(first.Payload).EventId;
        var secondId = Serializer.Deserialize<EdictEvent>(second.Payload).EventId;
        Assert.NotEqual(firstId, secondId);
    }

    static OutboxEntry PublishEntry(OrderPlacedEvent edictEvent) => new()
    {
        EntryId = Guid.NewGuid(),
        Kind = OutboxEffectKind.PublishEvent,
        Payload = Serializer.SerializeToArray<EdictEvent>(edictEvent),
        AttemptCount = 3,
        TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
    };

    static DeadLetterPromoter BuildPromoter()
    {
        var rowSerializer = SerializerProvider.GetRequiredService<ObjectSerializer>();
        var typeConverter = SerializerProvider.GetRequiredService<TypeConverter>();
        return new DeadLetterPromoter(
            Serializer,
            rowSerializer,
            new RowTypeResolver(typeConverter),
            new StubEdictEventStreamAccessors(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<DeadLetterPromoter>.Instance);
    }

    static IServiceProvider BuildSerializerProvider()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(DeadLetterPromoterEventIdTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider();
    }
}
