using Edict.Analyzers.Interceptors;

using Xunit;

namespace Edict.Analyzers.Tests.Interceptors;

public class BaseTypedRaiseAnalyzerTests
{
    [Fact]
    public void EDICT016_ShouldNotRaise_WhenRaiseCalledWithConcreteEvent()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
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
            public sealed class OrderState : IEdictPersistedState;
            public partial class OrderHandler : EdictCommandHandler<OrderState>
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder command)
                {
                    Raise(new OrderPlaced(command.OrderId));
                    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedRaiseAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT016_ShouldRaise_WhenRaiseCalledWithBaseTypedVariable()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
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
            public sealed class OrderState : IEdictPersistedState;
            public partial class OrderHandler : EdictCommandHandler<OrderState>
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder command)
                {
                    EdictEvent edictEvent = new OrderPlaced(command.OrderId);
                    Raise(edictEvent);
                    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedRaiseAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT016", diagnostic.Id);
        Assert.Contains("EdictEvent", diagnostic.GetMessage());
    }

    [Fact]
    public void EDICT016_ShouldNotRaise_WhenRaiseIsOnAnUnrelatedType()
    {
        // The analyzer keys on EdictCommandHandler, not on the method name alone.
        // A consumer type that declares its own Raise must not trip EDICT016.
        const string source = """
            using System;
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
                public void Use(NotAHandler notAHandler, Guid orderId)
                {
                    EdictEvent edictEvent = new OrderPlaced(orderId);
                    notAHandler.Raise(edictEvent);
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedRaiseAnalyzer());

        Assert.Empty(diagnostics);
    }
}
