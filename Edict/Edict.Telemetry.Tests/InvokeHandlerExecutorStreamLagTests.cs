using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.EventHandler;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Telemetry.Tests;

public sealed class InvokeHandlerExecutorStreamLagTests
{
    static readonly Serializer Serializer = BuildSerializer();

    [Fact]
    public async Task ExecuteAsync_ShouldRecordHandleLagInSeconds_AsAdvanceBetweenOccurredAtAndHandleTime()
    {
        var occurredAt = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(occurredAt);

        var captures = new List<Capture>();
        using var listener = StartListener(captures, typeof(LagSampleConsumer).FullName!);

        var edictEvent = new TelOrderPlacedEvent(
            OrderId: new Guid("22222222-2222-2222-2222-222222222222"),
            Sku: "LAG-WIDGET")
        {
            EventId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            OccurredAt = occurredAt,
        };
        var envelope = EnvelopeCodec.WrapInline(Serializer.SerializeToArray<EdictEvent>(edictEvent));
        var entry = new OutboxEntry
        {
            EntryId = Guid.NewGuid(),
            Kind = OutboxEffectKind.InvokeHandler,
            Payload = Serializer.SerializeToArray<EdictEvent>(envelope),
        };

        time.Advance(TimeSpan.FromSeconds(7));

        var executor = new InvokeHandlerExecutor(
            Serializer, new ClaimCheckUnwrap(Serializer, store: null), NoWriters, time);
        await executor.ExecuteAsync(
            entry, NullStreamProvider.Instance,
            _ => Task.CompletedTask,
            consumerType: typeof(LagSampleConsumer),
            liveWireEvent: null);

        var capture = Assert.Single(captures);
        Assert.Equal(7.0, capture.Value, precision: 3);
        Assert.Equal("TelOrderPlacedEvent", capture.Tag(SemanticConventions.Events.Tags.Type));
        Assert.Equal(typeof(LagSampleConsumer).FullName, capture.Tag(SemanticConventions.Common.Tags.GrainType));
    }

    static MeterListener StartListener(List<Capture> captures, string grainTypeFilter)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == EdictDiagnostics.SourceName
                    && inst.Name == SemanticConventions.Events.Meters.HandleLag)
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
            b.AddAssembly(typeof(InvokeHandlerExecutorStreamLagTests).Assembly);
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

    sealed class LagSampleConsumer { }

    sealed class NullStreamProvider : IStreamProvider
    {
        public static readonly NullStreamProvider Instance = new();
        public string Name => "edict";
        public bool IsRewindable => false;
        public IAsyncStream<T> GetStream<T>(StreamId streamId) =>
            throw new NotSupportedException();
    }
}
