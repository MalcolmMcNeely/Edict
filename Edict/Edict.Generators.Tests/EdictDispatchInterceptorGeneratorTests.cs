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
            public Task HandleAsync(OrderSubmitted edictEvent)
            {
                Dispatch(new AuthorizePayment(edictEvent.OrderId, edictEvent.Amount));
                return Task.CompletedTask;
            }

            public Task HandleAsync(PaymentDeclined edictEvent)
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
}
