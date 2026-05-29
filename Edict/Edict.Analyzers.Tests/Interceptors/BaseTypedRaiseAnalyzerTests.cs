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
            public partial class OrderHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(PlaceOrder c)
                {
                    Raise(new OrderPlaced(c.OrderId));
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
            public partial class OrderHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(PlaceOrder c)
                {
                    EdictEvent edictEvent = new OrderPlaced(c.OrderId);
                    Raise(edictEvent);
                    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedRaiseAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT016", d.Id);
        Assert.Contains("EdictEvent", d.GetMessage());
    }
}
