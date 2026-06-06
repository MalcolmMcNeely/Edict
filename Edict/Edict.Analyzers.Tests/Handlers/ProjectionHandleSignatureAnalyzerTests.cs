using Edict.Analyzers.Handlers;

using Xunit;

namespace Edict.Analyzers.Tests.Handlers;

public class ProjectionHandleSignatureAnalyzerTests
{
    [Fact]
    public void EDICT009_ShouldNotRaise_WhenProjectionHandleReturnsTaskWithEventParam()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;
            namespace Sample;
            public sealed class OrderProjection : IEdictPersistedState { }
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder<OrderProjection>
            {
                Task HandleAsync(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenStateProjectionHandleReturnsWrongType()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;
            namespace Sample;
            public sealed class OrderProjection : IEdictPersistedState { }
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder<OrderProjection>
            {
                Task<bool> HandleAsync(OrderPlacedEvent e) => Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 16 (0-indexed): "Task<bool> HandleAsync(OrderPlacedEvent e) => Task.FromResult(true);"
        Assert.Equal(16, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenListProjectionHandleReturnsWrongType()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Contracts.TableStorage;
            using Edict.Core.Projections;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class OrderStatusRow : IEdictPersistedState { }
            public partial class OrderListProjectionBuilder(IEdictTableStoreFactory factory)
                : EdictListProjectionBuilder<OrderStatusRow>(factory)
            {
                protected override string TableName => "orders";
                protected override string GetRowKey(EdictEvent edictEvent) => "row";
                Task<bool> HandleAsync(OrderPlacedEvent e) => Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        Assert.Contains("OrderListProjectionBuilder", d.GetMessage());
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenProjectionHandleParamIsNotEvent()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Persistence;
            using Edict.Core.Projections;
            namespace Sample;
            public sealed class OrderProjection : IEdictPersistedState { }
            public class NotAnEvent { }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder<OrderProjection>
            {
                Task HandleAsync(NotAnEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("NotAnEvent", d.GetMessage());
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 9 (0-indexed): "Task HandleAsync(NotAnEvent e) => Task.CompletedTask;"
        Assert.Equal(9, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
