using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictProjectionGeneratorTests
{
    const string SingleStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts;
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

        public sealed partial class OrderProjectionBuilder : EdictProjectionBuilderBase<EdictUnit>
        {
            Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
        }
        """;

    const string MultiEventSameStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts;
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

        public sealed partial class OrderProjectionBuilder : EdictProjectionBuilderBase<EdictUnit>
        {
            Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
            Task HandleAsync(OrderCancelledEvent edictEvent) => Task.CompletedTask;
        }
        """;

    // Both species in one compilation. The rename is silent-failure prone — a
    // root pointed at the wrong FQN compiles and emits nothing for one species
    // with no diagnostic — so the snapshot must show both grain spines AND a
    // read-route entry for each. Prior art the snapshot guards: discovery,
    // classification, dispatch-spine emission, and the read registrar.
    const string BothSpeciesConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Contracts.Persistence;
        using Edict.Contracts.TableStorage;
        using Edict.Core.Projections;
        using MessagePack;
        using Orleans;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [GenerateSerializer]
        [Alias("sample-order-status-row")]
        public sealed class OrderStatusRow : IEdictPersistedState
        {
            [Id(0)]
            public string Status { get; set; } = "";
        }

        [GenerateSerializer]
        [Alias("sample-delivery-status")]
        public sealed class DeliveryStatus : IEdictPersistedState
        {
            [Id(0)]
            public int MinutesAway { get; set; }
        }

        public sealed partial class OrdersByStatusProjectionBuilder(IEdictTableStoreFactory factory)
            : EdictListProjectionBuilder<OrderStatusRow>(factory)
        {
            protected override string TableName => "ordersbystatus";
            protected override string GetRowKey(EdictEvent edictEvent) => "status";
            Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
        }

        public sealed partial class DeliveryStatusProjectionBuilder : EdictProjectionBuilder<DeliveryStatus>
        {
            Task HandleAsync(OrderPlacedEvent edictEvent)
            {
                Projection.MinutesAway = 30;
                return Task.CompletedTask;
            }
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

    [Fact]
    public Task EdictProjectionGenerator_ShouldDiscoverAndEmitBothSpecies()
    {
        var generated = GeneratorTestHarness.RunProjectionGenerator(BothSpeciesConsumer);
        return Verify(generated);
    }
}
