using Edict.Analyzers.Handlers;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Edict.Analyzers.Tests.Handlers;

public class HandleMustBeHandleAsyncAnalyzerTests
{
    [Fact]
    public void EDICT018_ShouldRaise_OnCommandHandler_WhenMethodIsNamedHandle()
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
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT018", diagnostic.Id);
        Assert.Contains("HandleAsync", diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void EDICT018_ShouldRaise_OnEventHandler_WhenMethodIsNamedHandle()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Events;
            using Edict.Core.EventHandler;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderEmailEventHandler : EdictEventHandler
            {
                public Task Handle(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT018", diagnostic.Id);
        Assert.Contains("HandleAsync", diagnostic.GetMessage());
    }

    [Fact]
    public void EDICT018_ShouldRaise_OnSaga_WhenMethodIsNamedHandle()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Sagas;
            using Orleans;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            [GenerateSerializer]
            [Alias("OrderSagaProgress")]
            public sealed class OrderSagaProgress : IEdictPersistedState
            {
                [Id(0)] public int Step { get; set; }
            }
            public partial class OrderSaga : EdictSaga<OrderSagaProgress>
            {
                public Task Handle(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT018", diagnostic.Id);
        Assert.Contains("HandleAsync", diagnostic.GetMessage());
    }

    [Fact]
    public void EDICT018_ShouldRaise_OnProjectionBuilder_WhenMethodIsNamedHandle()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
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

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT018", diagnostic.Id);
        Assert.Contains("HandleAsync", diagnostic.GetMessage());
    }

    [Fact]
    public void EDICT018_ShouldNotRaise_WhenHandlerIsNamedHandleAsync()
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
                public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT018_ShouldNotRaise_OnNonHandlerClassWithHandleMethod()
    {
        const string source = """
            using System.Threading.Tasks;
            namespace Sample;
            public class DomainService
            {
                public Task<int> Handle(int input) => Task.FromResult(input);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT018_ShouldNotRaise_WhenHandleInheritsFromNonEdictBase()
    {
        const string source = """
            using System.Threading.Tasks;
            namespace Sample;
            public abstract class MvcControllerLike
            {
                public abstract Task<string> Handle(string input);
            }
            public sealed class MyController : MvcControllerLike
            {
                public override Task<string> Handle(string input) => Task.FromResult(input);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleMustBeHandleAsyncAnalyzer());

        Assert.Empty(diagnostics);
    }
}
