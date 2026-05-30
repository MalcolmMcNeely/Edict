using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictEventHandlerGeneratorTests
{
    const string SingleHandleConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.EventHandler;
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

        public sealed partial class OrderEmailHandler : EdictEventHandler
        {
            public Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
        }
        """;

    const string MultiHandleSameStreamConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.EventHandler;
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

        public sealed partial class OrderNotificationHandler : EdictEventHandler
        {
            public Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
            public Task HandleAsync(OrderCancelledEvent edictEvent) => Task.CompletedTask;
        }
        """;

    // Cross-assembly trap: the generator must emit FQN-only references and
    // keep the semantic walk syntax-scoped to the current compilation, so an
    // event type that lives in a separate contracts assembly still dispatches.
    const string CrossAssemblyContractConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.EventHandler;
        using MessagePack;
        using Sample.Shared.Contracts;

        namespace Sample.Shared.Contracts
        {
            [MessagePackObject(keyAsPropertyName: true)]
            [EdictStream("Notifications")]
            public sealed partial record CustomerNotifiedEvent(Guid CustomerId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid CustomerId { get; init; } = CustomerId;
            }
        }

        namespace Sample.Handlers
        {
            public sealed partial class CustomerEmailHandler : EdictEventHandler
            {
                public Task HandleAsync(CustomerNotifiedEvent edictEvent) => Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public Task EdictEventHandlerGenerator_ShouldEmitInterfaceSubscriptionHandlesTypeAndDispatch()
    {
        var generated = GeneratorTestHarness.RunEventHandlerGenerator(SingleHandleConsumer);
        return Verify(generated);
    }

    [Fact]
    public Task EdictEventHandlerGenerator_ShouldDeduplicateSubscriptionAndEmitEveryHandleArm_WhenMultipleHandlesShareStream()
    {
        var generated = GeneratorTestHarness.RunEventHandlerGenerator(MultiHandleSameStreamConsumer);
        return Verify(generated);
    }

    [Fact]
    public Task EdictEventHandlerGenerator_ShouldEmitDispatchAndHandlesType_WhenEventInDifferentNamespace()
    {
        var generated = GeneratorTestHarness.RunEventHandlerGenerator(CrossAssemblyContractConsumer);
        return Verify(generated);
    }
}
