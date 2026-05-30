using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictProjectionGeneratorTests
{
    const string SingleStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Projections;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
            public string Sku { get; init; } = Sku;
        }

        public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder
        {
            public Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
        }
        """;

    const string MultiEventSameStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Projections;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderCancelledEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder
        {
            public Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
            public Task HandleAsync(OrderCancelledEvent edictEvent) => Task.CompletedTask;
        }
        """;

    [Fact]
    public Task EdictProjectionGenerator_ShouldEmitInterfaceSubscriptionAndDispatch()
    {
        var generated = GeneratorTestHarness.RunProjectionGenerator(SingleStreamConsumer);
        return Verify(generated);
    }

    [Fact]
    public Task EdictProjectionGenerator_ShouldDeduplicateImplicitStreamSubscription_WhenSameStream()
    {
        var generated = GeneratorTestHarness.RunProjectionGenerator(MultiEventSameStreamConsumer);
        return Verify(generated);
    }
}
