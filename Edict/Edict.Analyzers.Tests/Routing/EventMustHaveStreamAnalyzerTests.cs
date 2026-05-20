using Edict.Analyzers.Routing;

using Xunit;

namespace Edict.Analyzers.Tests.Routing;

public class EventMustHaveStreamAnalyzerTests
{
    [Fact]
    public void EDICT008_ShouldNotRaise_WhenEventHasStreamAttribute()
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

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustHaveStreamAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT008_ShouldNotRaise_WhenAbstractEventSubtype()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            namespace Sample;
            public abstract record OrderEvent(Guid OrderId) : EdictEvent;
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustHaveStreamAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT008_ShouldRaiseOnType_WhenEventHasNoStreamAttribute()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustHaveStreamAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT008", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        // Line 4 (0-indexed): "public sealed partial record OrderPlacedEvent..."
        Assert.Equal(4, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
