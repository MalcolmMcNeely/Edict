using System.Diagnostics;

using Edict.Contracts;
using Edict.Contracts.Configuration;
using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.ClaimCheck;

// The faithful bug #2 reproduction: an oversized event raised through the real
// outbox path (enqueue stamping, claim-check wrap, the real PublishEventExecutor
// putting the pointer envelope on the wire) then unwrapped by the receiver. The
// dedup-committed id (envelope, keyed before the blob fetch) must equal the
// handler-visible id (the unwrapped inner event) and be non-empty.
public sealed class ClaimCheckOversizedEventIdFlowTests
{
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
    static readonly Guid OrderId = new("11111111-1111-1111-1111-111111111111");
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task OversizedRaisedEvent_PublishedThenUnwrapped_ShouldCarryNonEmptyIdEqualToEnvelopeId()
    {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var stream = new CapturingStreamProvider();
        var host = BuildHost(stream, store);
        // A consumer's Raise leaves EventId default; the oversized SKU forces the
        // claim-check path.
        var raised = new OrderPlacedEvent(OrderId, new string('x', 256)) { OccurredAt = Now };
        Assert.Equal(Guid.Empty, raised.EventId);

        // Act
        await host.EnqueueRaisedEventsAndDrainAsync([raised], traceParent: null, traceState: null);
        var envelope = Assert.IsType<EdictEventEnvelope>(Assert.Single(stream.Captured));
        var unwrap = new ClaimCheckUnwrap(Serializer, store);
        var materialised = await unwrap.ApplyAsync(envelope, consumerType: typeof(object), CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.Equal(envelope.EventId, materialised.EventId);
    }

    static OutboxHost<EdictUnit> BuildHost(CapturingStreamProvider stream, InMemoryClaimCheckStore store)
    {
        var log = new CallLog();
        var state = new CountingPersistentState<GrainEnvelope<EdictUnit>>(log);
        var accessors = new StubEdictEventStreamAccessors();
        var executor = new PublishEventExecutor(
            Serializer, accessors, new EventTagWriters(new Dictionary<Type, Action<EdictEvent, Activity>>()));
        // A low threshold with a real store forces every raised event onto the
        // claim-check pointer path.
        var policy = new ClaimCheckPolicy(Serializer, thresholdBytes: 64, store, accessors);
        return new OutboxHost<EdictUnit>(
            state,
            stream,
            new RecordingReminderRegistrar(log),
            [executor],
            new EdictOptions(),
            new FakeTimeProvider(Now),
            new NoopPromoter(),
            grainKey: "test-grain",
            grainTypeName: "TestGrain",
            claimCheckPolicy: policy);
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(ClaimCheckOversizedEventIdFlowTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed class NoopPromoter : IDeadLetterPromoter
    {
        public OutboxEntry Promote(OutboxEntry failed, Exception exception, string sourceGrainKey, string sourceGrainType, DateTimeOffset now) =>
            failed with { Kind = OutboxEffectKind.PublishEvent };
    }
}
