using Edict.Analyzers.Interceptors;

using Xunit;

namespace Edict.Analyzers.Tests.Interceptors;

public class OriginSendPrincipalAnalyzerTests
{
    static readonly string[] EnableEdict023 = ["EDICT023"];

    [Fact]
    public void EDICT023_ShouldRaise_OnBareOriginSend_WhenEnabled()
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

        var diagnostics = AnalyzerTestHarness.Run(source, EnableEdict023, new OriginSendPrincipalAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT023", diagnostic.Id);

        // The message must encode the user-versus-origin decision, not just "no principal":
        // resolve the real actor for a user send, mint one (such as system) for an actor-less
        // origin, or suppress where a registered resolver already attributes the site.
        var message = diagnostic.GetMessage();
        Assert.Contains("user-initiated", message, StringComparison.Ordinal);
        Assert.Contains("actor-less", message, StringComparison.Ordinal);
        Assert.Contains("system", message, StringComparison.Ordinal);
        Assert.Contains("resolver", message, StringComparison.Ordinal);
    }

    [Fact]
    public void EDICT023_ShouldNotRaise_OnBareOriginSend_WhenNotEnabled()
    {
        // The decisive constraint: the analyzer cannot see whether auditing is
        // wired, so it stays silent on the framework's own happy path until a
        // consumer opts in through the .editorconfig severity knob.
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

        var diagnostics = AnalyzerTestHarness.Run(source, new OriginSendPrincipalAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT023_ShouldNotRaise_OnExplicitPrincipalOverload()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Audit;
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
                    => sender.SendAsync(new PlaceOrder(orderId), EdictPrincipal.Of("system"));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, EnableEdict023, new OriginSendPrincipalAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT023_ShouldNotRaise_OnSagaDispatch()
    {
        // A relayed send a consumer writes inside a saga is 'Dispatch', a
        // different method; the analyzer keys on IEdictSender.SendAsync, so the
        // relay is exempt by construction with no special casing.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Sagas;
            namespace Sample;
            public sealed partial record AuthorizePayment(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            [EdictStream("Orders")]
            public sealed partial record OrderSubmitted(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class OrderProgress : IEdictPersistedState;
            public partial class OrderSaga : EdictSaga<OrderProgress>
            {
                public Task HandleAsync(OrderSubmitted edictEvent)
                {
                    Dispatch(new AuthorizePayment(edictEvent.OrderId));
                    return Task.CompletedTask;
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, EnableEdict023, new OriginSendPrincipalAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT023_ShouldNotRaise_OnEventRaise()
    {
        // Events are out of scope: Raise is reachable only inside a handler turn
        // already processing a principal-bearing command, so the event inherits
        // its actor and there is no consumer-written origin event-raise to flag.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Contracts.Persistence;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            [EdictStream("Orders")]
            public sealed partial record OrderPlaced(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class OrderState : IEdictPersistedState;
            public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder command)
                {
                    Raise(new OrderPlaced(command.OrderId));
                    return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
                }
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, EnableEdict023, new OriginSendPrincipalAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT023_ShouldNotRaise_OnLookAlikeSendAsyncOnUnrelatedType()
    {
        // The analyzer keys on IEdictSender, not on the method name alone. A
        // consumer type that declares its own single-arg SendAsync must not trip.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class NotASender
            {
                public Task SendAsync(EdictCommand command) => Task.CompletedTask;
            }
            public sealed class Caller
            {
                public Task Use(NotASender notASender, Guid orderId)
                    => notASender.SendAsync(new PlaceOrder(orderId));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, EnableEdict023, new OriginSendPrincipalAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT023_ShouldNotRaise_WhenSiteSuppressed()
    {
        // SuppressMessage is the documented escape for a resolver-backed origin
        // a consumer has confirmed is attributed at runtime.
        const string source = """
            using System;
            using System.Diagnostics.CodeAnalysis;
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
                [SuppressMessage("Edict", "EDICT023", Justification = "Resolver supplies the principal at runtime")]
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId)
                    => sender.SendAsync(new PlaceOrder(orderId));
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, EnableEdict023, new OriginSendPrincipalAnalyzer());

        Assert.Empty(diagnostics);
    }
}
