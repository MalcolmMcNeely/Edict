using Edict.Analyzers.Interceptors;

using Xunit;

namespace Edict.Analyzers.Tests.Interceptors;

public class BaseTypedSagaDispatchAnalyzerTests
{
    [Fact]
    public void EDICT017_ShouldNotRaise_WhenDispatchCalledWithConcreteCommand()
    {
        const string source = """
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
                public Task Handle(OrderSubmitted edictEvent)
                {
                    Dispatch(new AuthorizePayment(edictEvent.OrderId));
                    return Task.CompletedTask;
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSagaDispatchAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT017_ShouldRaise_WhenDispatchCalledWithBaseTypedVariable()
    {
        const string source = """
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
                public Task Handle(OrderSubmitted edictEvent)
                {
                    EdictCommand command = new AuthorizePayment(edictEvent.OrderId);
                    Dispatch(command);
                    return Task.CompletedTask;
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSagaDispatchAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT017", diagnostic.Id);
        Assert.Contains("EdictCommand", diagnostic.GetMessage());
    }

    [Fact]
    public void EDICT017_ShouldNotRaise_WhenDispatchIsOnAnUnrelatedType()
    {
        // The analyzer keys on EdictSaga, not on the method name alone. A
        // consumer type that happens to declare its own base-typed Dispatch
        // must not trip EDICT017.
        const string source = """
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
                public void Use(NotASaga notASaga, Guid orderId)
                {
                    EdictCommand command = new AuthorizePayment(orderId);
                    notASaga.Dispatch(command);
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSagaDispatchAnalyzer());

        Assert.Empty(diagnostics);
    }
}
