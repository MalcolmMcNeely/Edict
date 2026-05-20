using Edict.Analyzers.Routing;

using Xunit;

namespace Edict.Analyzers.Tests.Routing;

public class RouteKeyAnalyzerTests
{
    [Fact]
    public void EDICT003_ShouldNotRaise_WhenCommandHasOneGuidRouteKey()
    {
        var diagnostics = AnalyzerTestHarness.Run(AnalyzerTestSources.ValidBase, new RouteKeyAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnClass_WhenCommandHasNoRouteKey()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        // Line 3 (0-indexed): "public sealed record PlaceOrder..."
        Assert.Equal(3, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnEachProperty_WhenCommandHasMultipleRouteKeys()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId, Guid CorrelationId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [EdictRouteKey]
                public Guid CorrelationId { get; init; } = CorrelationId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("EDICT003", d.Id));
        Assert.All(diagnostics, d => Assert.Contains("PlaceOrder", d.GetMessage()));
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnProperty_WhenRouteKeyIsNotGuid()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(string OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public string OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderId", d.GetMessage());
        // Line 5 (0-indexed): "[EdictRouteKey]"  — attribute usage; property identifier on line 6
        Assert.Equal(6, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT003_ShouldNotRaise_WhenEventHasOneGuidRouteKey()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnClass_WhenEventHasNoRouteKey()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        // Line 4 (0-indexed): "public sealed partial record OrderPlacedEvent..."
        Assert.Equal(4, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnProperty_WhenEventRouteKeyIsNotGuid()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(string OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public string OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderId", d.GetMessage());
        // Line 8 (0-indexed): "public string OrderId { get; init; } = OrderId;"
        Assert.Equal(8, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
