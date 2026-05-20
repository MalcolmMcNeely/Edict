using Edict.Analyzers.Handlers;

using Xunit;

namespace Edict.Analyzers.Tests.Handlers;

public class HandleReturnTypeAnalyzerTests
{
    [Fact]
    public void EDICT002_ShouldNotRaise_WhenHandleReturnsTaskOfEdictCommandResult()
    {
        var diagnostics = AnalyzerTestHarness.Run(AnalyzerTestSources.ValidBase, new HandleReturnTypeAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT002_ShouldRaiseOnMethod_WhenHandleReturnsWrongType()
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
            public partial class OrderCommandHandler : EdictCommandHandler
            {
                public Task<bool> Handle(PlaceOrder c) =>
                    Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleReturnTypeAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT002", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        Assert.Contains("OrderCommandHandler", d.GetMessage());
        // Line 12 (0-indexed): "public Task<bool> Handle(PlaceOrder c) =>"
        Assert.Equal(12, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
