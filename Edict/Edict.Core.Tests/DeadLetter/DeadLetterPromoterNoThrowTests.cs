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

namespace Edict.Core.Tests.DeadLetter;

#pragma warning disable EDICT003, EDICT006
[Alias("DeadLetterPromoterNoThrowTests.NoRouteKeyCommand")]
[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record NoRouteKeyCommand : EdictCommand
{
    public Guid Id { get; init; }
}
#pragma warning restore EDICT003, EDICT006

public sealed class DeadLetterPromoterNoThrowTests
{
    static readonly Serializer Serializer = BuildSerializer();
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

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(DeadLetterPromoterNoThrowTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    sealed record Capture(long Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }
}
