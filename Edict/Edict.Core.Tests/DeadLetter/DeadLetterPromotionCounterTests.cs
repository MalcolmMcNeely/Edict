using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Core.Tests.DeadLetter;

public sealed class DeadLetterPromotionCounterTests
{
    static readonly Serializer Serializer = BuildSerializer();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Promote_ShouldIncrementPromotionCounter_TaggedWithEffectKindAndFailureReason()
    {
        var marker = $"DeadLetterPromoCounterTest_{Guid.NewGuid():N}";
        var captures = StartListener(marker);
        var promoter = BuildPromoter();

        promoter.Promote(PublishEventEntry(), new TimeoutException("downstream stalled"),
            "grain-key", marker, Now);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal("PublishEvent", measurement.Tag(SemanticConventions.Outbox.Tags.EffectKind));
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Timeout,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.FailureReason));
        Assert.Equal(marker, measurement.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public void Promote_ShouldBucketUnknownExceptionAsUnhandled()
    {
        var marker = $"DeadLetterPromoCounterTest_{Guid.NewGuid():N}";
        var captures = StartListener(marker);
        var promoter = BuildPromoter();

        promoter.Promote(PublishEventEntry(), new InvalidOperationException("nope"),
            "g", marker, Now);

        var measurement = Assert.Single(captures);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.FailureReason));
    }

    static OutboxEntry PublishEventEntry()
    {
        var evt = new OrderPlacedEvent(Guid.NewGuid(), "ITEM-1");
        return new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = Serializer.SerializeToArray<EdictEvent>(evt),
            AttemptCount = 3,
        };
    }

    static DeadLetterPromoter BuildPromoter()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new DeadLetterPromoter(Serializer, new StubEdictEventStreamAccessors(), services);
    }

    static List<Capture> StartListener(string grainTypeMarker)
    {
        var captures = new List<Capture>();
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.DeadLetter.Meters.PromotionCount)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            // Filter to this test's emission only — the global Meter is shared
            // across the test process; parallel tests would otherwise contaminate.
            var snapshot = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags)
            {
                snapshot[t.Key] = t.Value;
            }
            if ((snapshot.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                captures.Add(new Capture(value, snapshot));
            }
        });
        listener.Start();
        return captures;
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(DeadLetterPromotionCounterTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }
}
