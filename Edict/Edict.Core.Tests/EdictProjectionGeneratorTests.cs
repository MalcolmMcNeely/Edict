using static VerifyXunit.Verifier;

namespace Edict.Core.Tests;

public class EdictProjectionGeneratorTests
{
    private const string SingleStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Grains;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [Stream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : Event
        {
            [RouteKey]
            public Guid OrderId { get; init; } = OrderId;
            public string Sku { get; init; } = Sku;
        }

        public sealed partial class OrderProjectionGrain : ProjectionBuilderGrain
        {
            public Task Handle(OrderPlacedEvent evt) => Task.CompletedTask;
        }
        """;

    private const string MultiEventSameStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Grains;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [Stream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : Event
        {
            [RouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [MessagePackObject(keyAsPropertyName: true)]
        [Stream("Orders")]
        public sealed partial record OrderCancelledEvent(Guid OrderId) : Event
        {
            [RouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial class OrderProjectionGrain : ProjectionBuilderGrain
        {
            public Task Handle(OrderPlacedEvent evt) => Task.CompletedTask;
            public Task Handle(OrderCancelledEvent evt) => Task.CompletedTask;
        }
        """;

    [Fact]
    public Task Generator_emits_interface_subscription_and_dispatch()
    {
        var generated = GeneratorTestHarness.RunProjectionGenerator(SingleStreamConsumer);
        return Verify(generated);
    }

    [Fact]
    public Task Generator_deduplicates_ImplicitStreamSubscription_for_same_stream()
    {
        var generated = GeneratorTestHarness.RunProjectionGenerator(MultiEventSameStreamConsumer);
        return Verify(generated);
    }
}
