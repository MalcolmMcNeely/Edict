using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictRaiseInterceptorGeneratorTests
{
    const string RaiseConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Events;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [EdictStream("Orders")]
        public sealed partial record OrderCancelledEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            Task<EdictCommandResult> HandleAsync(PlaceOrder c)
            {
                Raise(new OrderPlacedEvent(c.OrderId));
                Raise(new OrderCancelledEvent(c.OrderId));
                return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
        }
        """;

    [Fact]
    public Task EdictRaiseInterceptorGenerator_ShouldEmitInterceptorPerEventType()
    {
        var generated = GeneratorTestHarness.RunRaiseInterceptorGenerator(RaiseConsumer);

        return Verify(generated);
    }

    [Fact]
    public void EdictRaiseInterceptorGenerator_ShouldEmitNothing_WhenInterceptorsDisabled()
    {
        var generated = GeneratorTestHarness.RunRaiseInterceptorGenerator(
            RaiseConsumer, interceptorsEnabled: false);

        Assert.Empty(generated);
    }

    [Fact]
    public void EdictRaiseInterceptorGenerator_ShouldEmitNoInterceptor_WhenRaiseIsOnAnUnrelatedType()
    {
        // Keys on EdictCommandHandler. A consumer type with its own Raise must
        // not be intercepted — a wrong stub here fails silently.
        const string consumer = """
            using System;

            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;

            namespace Sample;

            [EdictStream("Orders")]
            public sealed partial record OrderPlaced(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            public sealed class NotAHandler
            {
                public void Raise(EdictEvent edictEvent)
                {
                }
            }

            public sealed class Caller
            {
                public void Use(NotAHandler notAHandler, Guid orderId) =>
                    notAHandler.Raise(new OrderPlaced(orderId));
            }
            """;

        var generated = GeneratorTestHarness.RunRaiseInterceptorGenerator(consumer);

        Assert.DoesNotContain(generated, file => file.Key.EndsWith(".RaiseInterceptor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void EdictRaiseInterceptorGenerator_ShouldEmitNoInterceptor_WhenArgumentIsBaseTyped()
    {
        const string consumer = """
            using System;
            using System.Threading.Tasks;

            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Core.Commands;

            namespace Sample;

            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            [EdictStream("Orders")]
            public sealed partial record OrderPlaced(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            public partial class OrderCommandHandler : EdictCommandHandler
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder c)
                {
                    EdictEvent edictEvent = new OrderPlaced(c.OrderId);
                    Raise(edictEvent);
                    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
                }
            }
            """;

        var generated = GeneratorTestHarness.RunRaiseInterceptorGenerator(consumer);

        Assert.DoesNotContain(generated, file => file.Key.EndsWith(".RaiseInterceptor.g.cs", StringComparison.Ordinal));
    }
}
