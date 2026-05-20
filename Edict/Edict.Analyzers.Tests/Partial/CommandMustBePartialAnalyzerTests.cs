using Edict.Analyzers.Partial;

using Xunit;

namespace Edict.Analyzers.Tests.Partial;

public class CommandMustBePartialAnalyzerTests
{
    [Fact]
    public void EDICT006_ShouldNotRaise_WhenCommandIsPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new CommandMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT006_ShouldRaiseOnType_WhenConcreteCommandIsNotPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new CommandMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT006", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        // Line 3 (0-indexed): "public sealed record PlaceOrder..."
        Assert.Equal(3, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT006_ShouldNotRaise_WhenAbstractCommandSubtype()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public abstract record OrderCommand(Guid OrderId) : EdictCommand;
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new CommandMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }
}
