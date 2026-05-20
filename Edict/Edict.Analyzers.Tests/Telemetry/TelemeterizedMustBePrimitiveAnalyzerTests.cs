using Edict.Analyzers.Telemetry;

using Xunit;

namespace Edict.Analyzers.Tests.Telemetry;

public class TelemeterizedMustBePrimitiveAnalyzerTests
{
    [Fact]
    public void EDICT005_ShouldNotRaise_WhenTelemeterizedIsOnPrimitivePropertyOfEvent()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
            {
                [EdictTelemeterized]
                public string Sku { get; init; } = Sku;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT005_ShouldRaiseOnProperty_WhenTelemeterizedIsOnNonPrimitiveEventProperty()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            public class OrderDetails { public string Info { get; set; } = ""; }
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId, OrderDetails Details) : EdictEvent
            {
                [EdictTelemeterized]
                public OrderDetails Details { get; init; } = Details;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT005", d.Id);
        Assert.Contains("Details", d.GetMessage());
        // Line 9 (0-indexed): "public OrderDetails Details { get; init; } = Details;"
        Assert.Equal(9, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT005_ShouldNotRaise_WhenTelemeterizedIsOnPrimitiveProperty()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [EdictTelemeterized]
                public string Sku { get; init; } = Sku;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT005_ShouldRaiseOnProperty_WhenTelemeterizedIsOnNonPrimitive()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            public class OrderDetails { public string Info { get; set; } = ""; }
            public sealed record PlaceOrder(Guid OrderId, OrderDetails Details) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [EdictTelemeterized]
                public OrderDetails Details { get; init; } = Details;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT005", d.Id);
        Assert.Contains("Details", d.GetMessage());
        // Line 10 (0-indexed): "public OrderDetails Details { get; init; } = Details;"
        Assert.Equal(10, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
