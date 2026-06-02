using Edict.Analyzers.Routing;

using Xunit;

namespace Edict.Analyzers.Tests.Routing;

public class DuplicateCommandRouteAnalyzerTests
{
    [Fact]
    public void EDICT004_ShouldNotRaise_WhenEachCommandHandledByOneGrain()
    {
        var diagnostics = AnalyzerTestHarness.Run(AnalyzerTestSources.ValidBase, new DuplicateCommandRouteAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT004_ShouldRaiseOnSecondGrainHandle_WhenCommandIsDuplicated()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Results;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            public partial class DuplicateOrderCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new DuplicateCommandRouteAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT004", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        Assert.Contains("OrderCommandHandler", d.GetMessage());
        // Line 18 (0-indexed): "public Task<EdictCommandResult> Handle(PlaceOrder c) =>" in DuplicateOrderCommandHandler
        Assert.Equal(18, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT004_ShouldRaiseOnSecondGrainHandle_WhenGenericBaseCommandIsDuplicated()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Contracts.Results;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class OrderState : IEdictPersistedState;
            public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
            {
                public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            public partial class DuplicateOrderCommandHandler : EdictCommandHandler<OrderState>
            {
                public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new DuplicateCommandRouteAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT004", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        Assert.Contains("OrderCommandHandler", d.GetMessage());
        // Line 20 (0-indexed): "public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>" in DuplicateOrderCommandHandler
        Assert.Equal(20, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT004_ShouldNotRaise_ForAbstractIntermediateBasePlusConcreteSubclass()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Persistence;
            using Edict.Contracts.Results;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class OrderState : IEdictPersistedState;
            public abstract class OrderCommandHandlerBase : EdictCommandHandler<OrderState>
            {
                public abstract Task<EdictCommandResult> HandleAsync(PlaceOrder c);
            }
            public partial class OrderCommandHandler : OrderCommandHandlerBase
            {
                public override Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new DuplicateCommandRouteAnalyzer());

        Assert.Empty(diagnostics);
    }
}
