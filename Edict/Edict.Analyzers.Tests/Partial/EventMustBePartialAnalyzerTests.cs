using Edict.Analyzers.Partial;

using Xunit;

namespace Edict.Analyzers.Tests.Partial;

public class EventMustBePartialAnalyzerTests
{
    [Fact]
    public void EDICT007_ShouldNotRaise_WhenEventIsPartial()
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

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT007_ShouldNotRaise_WhenAbstractEventSubtype()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            namespace Sample;
            public abstract record OrderEvent(Guid OrderId) : EdictEvent;
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT007_ShouldRaiseOnType_WhenConcreteEventIsNotPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT007", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        // Line 5 (0-indexed): "public sealed record OrderPlacedEvent..."
        Assert.Equal(5, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
