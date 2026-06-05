using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictDispatchInterceptorGeneratorTests
{
    const string DispatchConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Events;
        using Edict.Contracts.Persistence;
        using Edict.Core.Sagas;

        namespace Sample;

        public sealed partial record AuthorizePayment(Guid OrderId, decimal Amount) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial record CancelOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [EdictStream("Orders")]
        public sealed partial record OrderSubmitted(Guid OrderId, decimal Amount) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [EdictStream("Payments")]
        public sealed partial record PaymentDeclined(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed class OrderPaymentProgress : IEdictPersistedState
        {
        }

        public partial class OrderPaymentSaga : EdictSaga<OrderPaymentProgress>
        {
            Task HandleAsync(OrderSubmitted edictEvent)
            {
                Dispatch(new AuthorizePayment(edictEvent.OrderId, edictEvent.Amount));
                return Task.CompletedTask;
            }

            Task HandleAsync(PaymentDeclined edictEvent)
            {
                Dispatch(new CancelOrder(edictEvent.OrderId));
                return Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public Task EdictDispatchInterceptorGenerator_ShouldEmitInterceptorPerCommandType()
    {
        var generated = GeneratorTestHarness.RunDispatchInterceptorGenerator(DispatchConsumer);

        return Verify(generated);
    }

    [Fact]
    public void EdictDispatchInterceptorGenerator_ShouldEmitNothing_WhenInterceptorsDisabled()
    {
        var generated = GeneratorTestHarness.RunDispatchInterceptorGenerator(
            DispatchConsumer, interceptorsEnabled: false);

        Assert.Empty(generated);
    }

    [Fact]
    public void EdictDispatchInterceptorGenerator_ShouldEmitNoInterceptor_WhenDispatchIsOnAnUnrelatedType()
    {
        // Keys on EdictSaga. A consumer type with its own Dispatch must not be
        // intercepted — a wrong stub here fails silently into the slow path.
        const string consumer = """
            using System;

            using Edict.Contracts.Commands;

            namespace Sample;

            public sealed partial record AuthorizePayment(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            public sealed class NotASaga
            {
                public void Dispatch(EdictCommand command)
                {
                }
            }

            public sealed class Caller
            {
                public void Use(NotASaga notASaga, Guid orderId) =>
                    notASaga.Dispatch(new AuthorizePayment(orderId));
            }
            """;

        var generated = GeneratorTestHarness.RunDispatchInterceptorGenerator(consumer);

        Assert.DoesNotContain(generated, file => file.Key.EndsWith(".DispatchInterceptor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void EdictDispatchInterceptorGenerator_ShouldEmitNoInterceptor_WhenArgumentIsBaseTyped()
    {
        const string consumer = """
            using System;
            using System.Threading.Tasks;

            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Sagas;

            namespace Sample;

            public sealed partial record AuthorizePayment(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            [EdictStream("Orders")]
            public sealed partial record OrderSubmitted(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            public sealed class OrderProgress : IEdictPersistedState
            {
            }

            public partial class OrderSaga : EdictSaga<OrderProgress>
            {
                Task HandleAsync(OrderSubmitted edictEvent)
                {
                    EdictCommand command = new AuthorizePayment(edictEvent.OrderId);
                    Dispatch(command);
                    return Task.CompletedTask;
                }
            }
            """;

        var generated = GeneratorTestHarness.RunDispatchInterceptorGenerator(consumer);

        Assert.DoesNotContain(generated, file => file.Key.EndsWith(".DispatchInterceptor.g.cs", StringComparison.Ordinal));
    }
}
