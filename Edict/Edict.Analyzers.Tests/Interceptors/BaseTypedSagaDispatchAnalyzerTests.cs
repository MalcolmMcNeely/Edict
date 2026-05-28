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
                public Task Handle(OrderSubmitted evt)
                {
                    Dispatch(new AuthorizePayment(evt.OrderId));
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
                public Task Handle(OrderSubmitted evt)
                {
                    EdictCommand cmd = new AuthorizePayment(evt.OrderId);
                    Dispatch(cmd);
                    return Task.CompletedTask;
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSagaDispatchAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT017", d.Id);
        Assert.Contains("EdictCommand", d.GetMessage());
    }
}
