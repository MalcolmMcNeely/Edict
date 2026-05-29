using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.EventHandler;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Telemetry;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.EventHandler;

public sealed class InvokeHandlerExecutorMetricsTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ExecuteAsync_ShouldRecordHandleDurationInSeconds_TaggedWithEventTypeAndGrainType()
    {
        var captures = new List<Capture>();
        using var listener = StartListener(captures, typeof(SampleConsumer).FullName!);

        var evt = new OrderPlacedEvent(
            OrderId: new Guid("11111111-1111-1111-1111-111111111111"),
            Sku: "WIDGET")
        {
            EventId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        };
        var inlineBytes = Serializer.SerializeToArray<EdictEvent>(evt);
        var envelope = EnvelopeCodec.WrapInline(inlineBytes);
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        var executor = new InvokeHandlerExecutor(Serializer, new ClaimCheckUnwrap(Serializer, store: null), NoWriters, TimeProvider.System);
        await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance,
            async e => await Task.Delay(TimeSpan.FromMilliseconds(5)),
            consumerType: typeof(SampleConsumer),
            liveWireEvent: null);

        var capture = Assert.Single(captures);
        Assert.True(capture.Value > 0, "duration should be positive");
        Assert.True(capture.Value < 60, "duration must be in seconds (a 5ms delay can't be 60+)");
        Assert.Equal("OrderPlacedEvent", capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(typeof(SampleConsumer).FullName, capture.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecordDuration_EvenWhenDispatchThrows()
    {
        var captures = new List<Capture>();
        using var listener = StartListener(captures, typeof(SampleConsumerBoom).FullName!);

        var evt = new OrderPlacedEvent(
            OrderId: new Guid("33333333-3333-3333-3333-333333333333"),
            Sku: "BOOM");
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(evt));
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        var executor = new InvokeHandlerExecutor(Serializer, new ClaimCheckUnwrap(Serializer, store: null), NoWriters, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            entry, NullStreamProvider.Instance,
            _ => throw new InvalidOperationException("simulated"),
            consumerType: typeof(SampleConsumerBoom),
            liveWireEvent: null));

        var capture = Assert.Single(captures);
        Assert.True(capture.Value >= 0);
        Assert.Equal("OrderPlacedEvent", capture.Tag(SemanticConventions.Events.Tags.Type));
    }

    static MeterListener StartListener(List<Capture> captures, string grainTypeFilter)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.Events.Meters.HandleDuration)
                {
                    l.EnableMeasurementEvents(inst);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags) { dict[t.Key] = t.Value; }
            if ((dict.GetValueOrDefault(SemanticConventions.Common.Tags.GrainType) as string) == grainTypeFilter)
            {
                captures.Add(new Capture(value, dict));
            }
        });
        listener.Start();
        return listener;
    }

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(b =>
        {
            b.AddAssembly(typeof(InvokeHandlerExecutorMetricsTests).Assembly);
            b.AddEdictContractSerializer();
        });
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    static readonly IEventTagWriters NoWriters =
        new EventTagWriters(new Dictionary<Type, Action<EdictEvent, Activity>>());

    sealed record Capture(double Value, IReadOnlyDictionary<string, object?> Tags)
    {
        public object? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    }

    sealed class SampleConsumer { }
    sealed class SampleConsumerBoom { }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException();
    }
}
