using System.Diagnostics;

using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;

using Xunit;

namespace Edict.Core.Tests.DeadLetter;

// A dead-letter promotion runs in a decoupled drain turn, not the command turn that
// produced the failure, so edict.dead_letter.promote is a new trace root that links
// back to the failing entry's persisted W3C context rather than nesting under it.
// The link lets an operator pivot from a promoted dead-letter straight to the
// originating command trace.
public sealed class DeadLetterPromoteSpanTests
{
    static readonly IServiceProvider SerializerProvider = BuildSerializerProvider();
    static readonly Serializer Serializer = SerializerProvider.GetRequiredService<Serializer>();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Promote_ShouldEmitNewRootLinkingToTheEntrysPersistedContext_CarryingPromotionTags()
    {
        var stopped = new List<Activity>();
        using var listener = StartPromoteListener(stopped);

        // A trace context unique to this test, so the process-wide listener selects
        // this promotion's span by its link target rather than by name — sibling test
        // classes promote concurrently and emit their own edict.dead_letter.promote spans.
        var originatingTraceId = ActivityTraceId.CreateRandom();
        var originatingSpanId = ActivitySpanId.CreateRandom();
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = Serializer.SerializeToArray<EdictEvent>(new OrderPlacedEvent(Guid.NewGuid(), "SKU-1")),
            AttemptCount = 3,
            TraceParent = ActivityExtensions.BuildTraceParent(originatingTraceId.ToHexString(), originatingSpanId.ToHexString()),
        };
        var exception = new InvalidOperationException("downstream");

        var promoter = BuildPromoter();
        promoter.Promote(entry, exception, "grain-key", "Sample.OrderCommandHandler", Now);

        Activity promote;
        lock (stopped)
        {
            promote = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.DeadLetter.Spans.Promote} Sample.OrderCommandHandler"
                && a.Links.Any(link => link.Context.SpanId == originatingSpanId));
        }

        // The promotion is its own trace — it does not share the originating command's.
        Assert.Null(promote.Parent);
        Assert.NotEqual(originatingTraceId, promote.TraceId);

        // ...but it links back to the failing entry's persisted W3C context.
        var onlyLink = Assert.Single(promote.Links);
        Assert.Equal(originatingTraceId, onlyLink.Context.TraceId);
        Assert.Equal(originatingSpanId, onlyLink.Context.SpanId);

        Assert.Equal("Sample.OrderCommandHandler", promote.GetTagItem(SemanticConventions.Common.Tags.GrainType));
        Assert.Equal(
            OutboxEffectKind.PublishEvent.ToString(),
            promote.GetTagItem(SemanticConventions.Outbox.Tags.EffectKind));
        Assert.Equal(
            DeadLetterFailureClassifier.Classify(exception, []),
            promote.GetTagItem(SemanticConventions.DeadLetter.Tags.FailureReason));
    }

    [Fact]
    public void PromoteScheduleTimeout_ShouldEmitNewRootLinkingToTheArmContext_CarryingScheduleTimeoutReason()
    {
        var stopped = new List<Activity>();
        using var listener = StartPromoteListener(stopped);

        var armTraceId = ActivityTraceId.CreateRandom();
        var armSpanId = ActivitySpanId.CreateRandom();

        var promoter = BuildPromoter();
        promoter.PromoteScheduleTimeout(
            scheduleMessageType: "Sample.FulfillNextLine",
            sourceGrainKey: "grain-key",
            sourceGrainType: "Sample.OrderCommandHandler",
            traceParent: ActivityExtensions.BuildTraceParent(armTraceId.ToHexString(), armSpanId.ToHexString()),
            traceState: null,
            now: Now);

        Activity promote;
        lock (stopped)
        {
            promote = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.DeadLetter.Spans.Promote} Sample.OrderCommandHandler"
                && a.Links.Any(link => link.Context.SpanId == armSpanId));
        }

        Assert.Null(promote.Parent);
        Assert.NotEqual(armTraceId, promote.TraceId);

        var onlyLink = Assert.Single(promote.Links);
        Assert.Equal(armTraceId, onlyLink.Context.TraceId);
        Assert.Equal(armSpanId, onlyLink.Context.SpanId);

        Assert.Equal("Sample.OrderCommandHandler", promote.GetTagItem(SemanticConventions.Common.Tags.GrainType));
        Assert.Equal(
            OutboxEffectKind.PublishEvent.ToString(),
            promote.GetTagItem(SemanticConventions.Outbox.Tags.EffectKind));
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.ScheduleTimeout,
            promote.GetTagItem(SemanticConventions.DeadLetter.Tags.FailureReason));
    }

    [Fact]
    public void Promote_ShouldNotThrow_AndStillReturnARow_WhenTraceParentIsMalformed_WithListenerActive()
    {
        var stopped = new List<Activity>();
        using var listener = StartPromoteListener(stopped);

        // The link carrier is built off the persisted traceparent before the
        // body-materialise try/catch, so a garbage traceparent must not throw out
        // of the safety net — it degrades to a span with no link, never a poison
        // pill. The ids are well-formed length but non-hex.
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = Serializer.SerializeToArray<EdictEvent>(new OrderPlacedEvent(Guid.NewGuid(), "SKU-1")),
            AttemptCount = 3,
            TraceParent = $"00-{new string('z', 32)}-{new string('z', 16)}-01",
        };

        var promoter = BuildPromoter();
        var promoted = promoter.Promote(entry, new InvalidOperationException("downstream"), "grain-key", "Sample.OrderCommandHandler", Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
    }

    static ActivityListener StartPromoteListener(List<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

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
            builder.AddAssembly(typeof(DeadLetterPromoteSpanTests).Assembly);
            builder.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider();
    }
}
