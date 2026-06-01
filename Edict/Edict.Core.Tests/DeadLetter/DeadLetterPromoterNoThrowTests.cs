using System.Diagnostics.Metrics;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using MessagePack;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans;
using Orleans.Serialization;
using Orleans.Serialization.TypeSystem;

namespace Edict.Core.Tests.DeadLetter;

#pragma warning disable EDICT003, EDICT006
[Alias("DeadLetterPromoterNoThrowTests.NoRouteKeyCommand")]
[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record NoRouteKeyCommand : EdictCommand
{
    public Guid Id { get; init; }
}
#pragma warning restore EDICT003, EDICT006

// The Explode getter throws, but [IgnoreMember] keeps it off the MessagePack
// wire so the message round-trips through the Orleans serializer; only the
// promoter's JsonSerializer.Serialize pass invokes it, modelling a consumer
// payload that defeats forensic JSON rendering.
[EdictStream("PromoterNoThrowJsonFail")]
public sealed partial record JsonUnserialisableEvent : EdictEvent
{
    [EdictRouteKey]
    public Guid RouteKey { get; init; }

    [IgnoreMember]
    public string Explode => throw new InvalidOperationException("payload getter blew up during promotion");
}

public sealed partial record JsonUnserialisableCommand : EdictCommand
{
    [EdictRouteKey]
    public Guid RouteKey { get; init; }

    [IgnoreMember]
    public string Explode => throw new InvalidOperationException("payload getter blew up during promotion");
}

// Orleans serializes only [Id]-marked members, so the throwing computed
// property rides the row wire invisibly and resolves on drain; the promoter's
// JsonSerializer.Serialize pass over the materialised row is the only caller
// that invokes the getter.
[GenerateSerializer]
[Alias("DeadLetterPromoterNoThrowTests.JsonUnserialisableRow")]
public sealed record JsonUnserialisableRow
{
    [Id(0)]
    public string Value { get; init; } = "";

    public string Explode => throw new InvalidOperationException("row getter blew up during promotion");
}

public sealed class DeadLetterPromoterNoThrowTests
{
    static readonly IServiceProvider SerializerProvider = BuildSerializerProvider();
    static readonly Serializer Serializer = SerializerProvider.GetRequiredService<Serializer>();
    static readonly ObjectSerializer RowSerializer = SerializerProvider.GetRequiredService<ObjectSerializer>();
    static readonly TypeConverter TypeConverter = SerializerProvider.GetRequiredService<TypeConverter>();
    static readonly DateTimeOffset Now = new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Promote_ShouldNotThrow_AndReturnSyntheticRow_WhenEffectKindIsUnknown()
    {
        var marker = $"PromoterNoThrowTest_{Guid.NewGuid():N}";
        var captures = StartFailureListener(marker);
        var promoter = BuildPromoter();
        var unknownKindEntry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = (OutboxEffectKind)99,
            Payload = [],
            AttemptCount = 3,
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        var promoted = promoter.Promote(
            unknownKindEntry, new InvalidOperationException("not used"),
            sourceGrainKey: "grain-key",
            sourceGrainType: marker,
            now: Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.Equal(nameof(EdictUnsupportedEffectKindException), raised.ExceptionType);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.UnsupportedKind,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.PromotionFailureReason));
    }

    [Fact]
    public void Promote_ShouldNotThrow_AndReturnSyntheticRow_WhenSendCommandLacksRouteKey()
    {
        var marker = $"PromoterNoThrowTest_{Guid.NewGuid():N}";
        var captures = StartFailureListener(marker);
        var promoter = BuildPromoter(WithRouteFor<NoRouteKeyCommand>("Sample.NoRouteHandler"));
        var command = new NoRouteKeyCommand { Id = Guid.NewGuid() };
        var sendCommandEntry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.SendCommand,
            Payload = Serializer.SerializeToArray<EdictCommand>(command),
            AttemptCount = 3,
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        var promoted = promoter.Promote(
            sendCommandEntry, new InvalidOperationException("downstream"),
            sourceGrainKey: "grain-key",
            sourceGrainType: marker,
            now: Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.Equal(nameof(EdictMissingRouteKeyException), raised.ExceptionType);
        Assert.Equal($"Sample.NoRouteHandler/{Guid.Empty:D}", raised.EffectTarget);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.MissingRouteKey,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.PromotionFailureReason));
    }

    [Fact]
    public void Promote_ShouldNotThrow_AndReturnSyntheticRow_WhenUpsertRowTypeNoLongerResolves()
    {
        var marker = $"PromoterNoThrowTest_{Guid.NewGuid():N}";
        var captures = StartFailureListener(marker);
        var promoter = BuildPromoter();
        var effect = new UpsertRowEffect
        {
            TableName = "orders-by-status",
            PartitionKey = "pk",
            RowKey = "rk",
            RowAlias = "Edict.Tests.RenamedAwayRowAliasShouldNotResolve",
            RowBytes = [1, 2, 3],
        };
        var upsertRowEntry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.UpsertRow,
            Payload = Serializer.SerializeToArray(effect),
            AttemptCount = 3,
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        var promoted = promoter.Promote(
            upsertRowEntry, new InvalidOperationException("table-write"),
            sourceGrainKey: "grain-key",
            sourceGrainType: marker,
            now: Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.Equal(nameof(EdictPromotionSerializationException), raised.ExceptionType);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.SerializationFailure,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.PromotionFailureReason));
    }

    [Fact]
    public void Promote_ShouldNotThrow_AndReturnSyntheticRow_WhenPublishEventPayloadFailsJsonSerialisation()
    {
        var marker = $"PromoterNoThrowTest_{Guid.NewGuid():N}";
        var captures = StartFailureListener(marker);
        var promoter = BuildPromoter();
        var edictEvent = new JsonUnserialisableEvent { RouteKey = Guid.NewGuid() };
        var publishEventEntry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.PublishEvent,
            Payload = Serializer.SerializeToArray<EdictEvent>(edictEvent),
            AttemptCount = 3,
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        var promoted = promoter.Promote(
            publishEventEntry, new InvalidOperationException("stream-publish"),
            sourceGrainKey: "grain-key",
            sourceGrainType: marker,
            now: Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.Equal(nameof(EdictPromotionSerializationException), raised.ExceptionType);
        Assert.Null(raised.PayloadJson);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.SerializationFailure,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.PromotionFailureReason));
    }

    [Fact]
    public void Promote_ShouldNotThrow_AndReturnSyntheticRow_WhenSendCommandPayloadFailsJsonSerialisation()
    {
        var marker = $"PromoterNoThrowTest_{Guid.NewGuid():N}";
        var captures = StartFailureListener(marker);
        var promoter = BuildPromoter(WithRouteFor<JsonUnserialisableCommand>("Sample.UnserialisableHandler"));
        var command = new JsonUnserialisableCommand { RouteKey = Guid.NewGuid() };
        var sendCommandEntry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.SendCommand,
            Payload = Serializer.SerializeToArray<EdictCommand>(command),
            AttemptCount = 3,
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        var promoted = promoter.Promote(
            sendCommandEntry, new InvalidOperationException("downstream"),
            sourceGrainKey: "grain-key",
            sourceGrainType: marker,
            now: Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.Equal(nameof(EdictPromotionSerializationException), raised.ExceptionType);
        Assert.Null(raised.PayloadJson);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.SerializationFailure,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.PromotionFailureReason));
    }

    [Fact]
    public void Promote_ShouldNotThrow_AndReturnSyntheticRow_WhenUpsertRowPayloadFailsJsonSerialisation()
    {
        var marker = $"PromoterNoThrowTest_{Guid.NewGuid():N}";
        var captures = StartFailureListener(marker);
        var promoter = BuildPromoter();
        var effect = new UpsertRowEffect
        {
            TableName = "orders-by-status",
            PartitionKey = "pk",
            RowKey = "rk",
            RowAlias = TypeConverter.Format(typeof(JsonUnserialisableRow)),
            RowBytes = Serializer.SerializeToArray<object>(new JsonUnserialisableRow { Value = "round-trip" }),
        };
        var upsertRowEntry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.UpsertRow,
            Payload = Serializer.SerializeToArray(effect),
            AttemptCount = 3,
            TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        };

        var promoted = promoter.Promote(
            upsertRowEntry, new InvalidOperationException("table-write"),
            sourceGrainKey: "grain-key",
            sourceGrainType: marker,
            now: Now);

        Assert.Equal(OutboxEffectKind.PublishEvent, promoted.Kind);
        var raised = Assert.IsType<EdictDeadLetterRaised>(Serializer.Deserialize<EdictEvent>(promoted.Payload));
        Assert.Equal(nameof(EdictPromotionSerializationException), raised.ExceptionType);
        Assert.Null(raised.PayloadJson);

        var measurement = Assert.Single(captures);
        Assert.Equal(1L, measurement.Value);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.PromotionFailureReasonValues.SerializationFailure,
            measurement.Tag(SemanticConventions.DeadLetter.Tags.PromotionFailureReason));
    }

    static DeadLetterPromoter BuildPromoter(params CommandRoute[] routes)
    {
        var collection = new ServiceCollection();
        if (routes.Length > 0)
        {
            var resolver = new CommandRouteResolver(routes.ToDictionary(r => r.CommandType));
            collection.AddSingleton(resolver);
        }
        var services = collection.BuildServiceProvider();
        return new DeadLetterPromoter(
            Serializer,
            RowSerializer,
            new RowTypeResolver(TypeConverter),
            new StubEdictEventStreamAccessors(),
            services,
            NullLogger<DeadLetterPromoter>.Instance);
    }

    static CommandRoute WithRouteFor<TCommand>(string grainClassName)
        where TCommand : EdictCommand =>
        new(typeof(TCommand), typeof(IFakeGrainInterface), grainClassName,
            _ => Guid.Empty);

    interface IFakeGrainInterface;

    static List<Capture> StartFailureListener(string grainTypeMarker)
    {
        var captures = new List<Capture>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == EdictDiagnostics.SourceName
                    && instrument.Name == SemanticConventions.DeadLetter.Meters.PromotionFailureCount)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var snapshot = new Dictionary<string, object?>(tags.Length);
            foreach (var tag in tags)
            {
                snapshot[tag.Key] = tag.Value;
            }
            if ((snapshot.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeMarker)
            {
                captures.Add(new Capture(value, snapshot));
            }
        });
        listener.Start();
        return captures;
    }

    static IServiceProvider BuildSerializerProvider()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(DeadLetterPromoterNoThrowTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider();
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }
}
