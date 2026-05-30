using Edict.Analyzers.Interceptors;

using Xunit;

namespace Edict.Analyzers.Tests.Interceptors;

public class BaseTypedSendAnalyzerTests
{
    [Fact]
    public void EDICT015_ShouldNotRaise_WhenSendCalledWithConcreteCommand()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId)
                    => sender.SendAsync(new PlaceOrder(orderId));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSendAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT015_ShouldRaise_WhenSendCalledWithBaseTypedVariable()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId)
                {
                    EdictCommand command = new PlaceOrder(orderId);
                    return sender.SendAsync(command);
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSendAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT015", d.Id);
        Assert.Contains("EdictCommand", d.GetMessage());
    }

    [Fact]
    public void EDICT015_ShouldRaise_WhenSendCalledWithAbstractSubclassVariable()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            namespace Sample;
            public abstract record OrderCommandBase(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed record PlaceOrder(Guid OrderId) : OrderCommandBase(OrderId);
            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId)
                {
                    OrderCommandBase command = new PlaceOrder(orderId);
                    return sender.SendAsync(command);
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSendAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT015", d.Id);
        Assert.Contains("OrderCommandBase", d.GetMessage());
    }

    [Fact]
    public void EDICT015_ShouldNotRaise_OnFrameworkPattern_WhenSiteSuppressed()
    {
        // Mirrors Edict.Core.Outbox.SendCommandExecutor: the deferred path
        // deserialises an EdictCommand from persisted state and sends it
        // through IEdictSender.Send. Edict.Core does not reference the
        // analyzer assembly so the framework's own copy of this pattern is
        // never seen; the SuppressMessage attribute is the documented escape
        // hatch for any consumer who genuinely needs the deferred shape.
        const string source = """
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;
            namespace Sample;
            public sealed class DeferredDispatcher
            {
                [SuppressMessage("Edict", "EDICT015", Justification = "Deferred dispatch from persisted state")]
                public Task<EdictCommandResult> Dispatch(IEdictSender sender, EdictCommand command)
                    => sender.SendAsync(command);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new BaseTypedSendAnalyzer());

        Assert.Empty(diagnostics);
    }
}
