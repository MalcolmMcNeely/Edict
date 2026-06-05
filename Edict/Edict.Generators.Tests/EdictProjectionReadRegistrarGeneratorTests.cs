using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictProjectionReadRegistrarGeneratorTests
{
    const string ListProjectionConsumer = """
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

        public sealed partial class OrderProjectionBuilder(IEdictTableStoreFactory factory)
            : EdictListProjectionBuilder<OrderStatusRow>(factory)
        {
            protected override string TableName => "ordersbystatus";
            protected override string GetRowKey(EdictEvent edictEvent) => "status";
            Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
        }
        """;

    // A projection on the abstract root with no List row contributes no read
    // route — the read facade only addresses row-bearing List projections.
    const string RootProjectionConsumer = """
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Projections;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(System.Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public System.Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder
        {
            Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
        }
        """;

    [Fact]
    public Task EdictProjectionReadRegistrar_ShouldMapRowTypeToGrainClassName()
    {
        var generated = GeneratorTestHarness.RunProjectionReadRegistrarGenerator(ListProjectionConsumer);
        return Verify(generated);
    }

    [Fact]
    public void EdictProjectionReadRegistrar_ShouldNotEmit_ForRootProjectionWithoutRow()
    {
        var generated = GeneratorTestHarness.RunProjectionReadRegistrarGenerator(RootProjectionConsumer);
        Assert.Empty(generated);
    }
}
