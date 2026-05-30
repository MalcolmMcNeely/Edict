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
            using Edict.Core.Projections;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder
            {
                public Task HandleAsync(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenProjectionHandleReturnsWrongType()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Core.Projections;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder
            {
                public Task<bool> HandleAsync(OrderPlacedEvent e) => Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 14 (0-indexed): "public Task<bool> HandleAsync(OrderPlacedEvent e) => Task.FromResult(true);"
        Assert.Equal(14, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenProjectionHandleParamIsNotEvent()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Events;
            using Edict.Core.Projections;
            namespace Sample;
            public class NotAnEvent { }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder
            {
                public Task HandleAsync(NotAnEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("NotAnEvent", d.GetMessage());
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 8 (0-indexed): "public Task HandleAsync(NotAnEvent e) => Task.CompletedTask;"
        Assert.Equal(8, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
