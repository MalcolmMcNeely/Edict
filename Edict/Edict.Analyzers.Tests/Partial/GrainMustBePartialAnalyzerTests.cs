using Edict.Analyzers.Partial;

using Xunit;

namespace Edict.Analyzers.Tests.Partial;

public class GrainMustBePartialAnalyzerTests
{
    [Fact]
    public void EDICT001_ShouldNotRaise_WhenGrainIsPartial()
    {
        var diagnostics = AnalyzerTestHarness.Run(AnalyzerTestSources.ValidBase, new GrainMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT001_ShouldRaiseOnClassIdentifier_WhenGrainIsNotPartial()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public class OrderCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT001", d.Id);
        Assert.Contains("OrderCommandHandler", d.GetMessage());
        // Line 10 (0-indexed): "public class OrderCommandHandler : EdictCommandHandler"
        Assert.Equal(10, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT001_ShouldNotRaise_WhenProjectionBuilderGrainIsPartial()
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
                public Task Handle(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT001_ShouldRaiseOnClassIdentifier_WhenProjectionBuilderGrainIsNotPartial()
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
            public class OrderProjectionBuilder : EdictProjectionBuilder
            {
                public Task Handle(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT001", d.Id);
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 12 (0-indexed): "public class OrderProjectionBuilder : EdictProjectionBuilder"
        Assert.Equal(12, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
