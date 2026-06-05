using Edict.Analyzers.Handlers;
using Edict.Analyzers.Partial;
using Edict.Analyzers.Routing;
using Edict.Analyzers.Validation;

using Microsoft.CodeAnalysis.Diagnostics;

using Xunit;

namespace Edict.Analyzers.Tests.Guards;

/// <summary>
/// The explicit tripwire for the "match base type by FQN but ignore genericity"
/// failure class that produced the EDICT001/002/004 rot. Every base-chain analyzer
/// is run against the canonical generic consumer shape every Sample handler uses
/// (<c>EdictCommandHandler&lt;TState&gt;</c>): the well-formed case must stay clean,
/// and a single malformed mutation must be caught by the expected diagnostic.
/// If any analyzer regresses to generics-naive base matching it stops
/// seeing the generic shape, the malformed mutation goes unflagged, and the
/// matching guard here fails.
/// </summary>
public class CanonicalGenericBaseGuardTests
{
    const string Header = """
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
        """;

    const string WellFormedHandler = """
        public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
        {
            Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;

    const string WellFormedValidator = """
        public sealed class PlaceOrderCommandValidator : EdictCommandValidator<PlaceOrder>
        {
        }
        """;

    const string CanonicalShape = Header + "\n" + WellFormedHandler + "\n" + WellFormedValidator;

    [Fact]
    public void CanonicalGenericShape_RaisesNoBaseChainDiagnostics()
    {
        var diagnostics = AnalyzerTestHarness.Run(
            CanonicalShape,
            new GrainMustBePartialAnalyzer(),
            new HandleReturnTypeAnalyzer(),
            new DuplicateCommandRouteAnalyzer(),
            new HandleMustBeHandleAsyncAnalyzer(),
            new DuplicateCommandValidatorAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT001_IsCaught_WhenGenericHandlerIsNotPartial()
    {
        const string nonPartialHandler = """
            public class OrderCommandHandler : EdictCommandHandler<OrderState>
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        AssertMalformedShapeIsCaught(
            Header + "\n" + nonPartialHandler,
            new GrainMustBePartialAnalyzer(),
            "EDICT001");
    }

    [Fact]
    public void EDICT002_IsCaught_WhenGenericHandlerReturnsWrongType()
    {
        const string wrongReturnHandler = """
            public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
            {
                Task<bool> HandleAsync(PlaceOrder command) =>
                    Task.FromResult(true);
            }
            """;

        AssertMalformedShapeIsCaught(
            Header + "\n" + wrongReturnHandler,
            new HandleReturnTypeAnalyzer(),
            "EDICT002");
    }

    [Fact]
    public void EDICT004_IsCaught_WhenGenericCommandIsRoutedTwice()
    {
        const string duplicateHandler = """
            public partial class SecondOrderCommandHandler : EdictCommandHandler<OrderState>
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        AssertMalformedShapeIsCaught(
            Header + "\n" + WellFormedHandler + "\n" + duplicateHandler,
            new DuplicateCommandRouteAnalyzer(),
            "EDICT004");
    }

    [Fact]
    public void EDICT018_IsCaught_WhenGenericHandlerMethodIsNamedHandle()
    {
        const string oldNameHandler = """
            public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
            {
                public Task<EdictCommandResult> Handle(PlaceOrder command) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        AssertMalformedShapeIsCaught(
            Header + "\n" + oldNameHandler,
            new HandleMustBeHandleAsyncAnalyzer(),
            "EDICT018");
    }

    [Fact]
    public void EDICT019_IsCaught_WhenGenericCommandIsValidatedTwice()
    {
        const string secondValidator = """
            public sealed class SecondPlaceOrderCommandValidator : EdictCommandValidator<PlaceOrder>
            {
            }
            """;

        AssertMalformedShapeIsCaught(
            Header + "\n" + WellFormedValidator + "\n" + secondValidator,
            new DuplicateCommandValidatorAnalyzer(),
            "EDICT019");
    }

    [Fact]
    public void NonGenericShimBase_IsStillMatchedByBaseChainAnalyzer()
    {
        // The non-generic EdictCommandHandler shim (EdictCommandHandler : EdictCommandHandler<EdictUnit>)
        // remains a supported base. This is the single deliberate non-generic fixture, kept to prove the
        // base-chain analyzers still fire on the shim and not only on the canonical generic shape.
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
            public class OrderCommandHandler : EdictCommandHandler
            {
                Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        AssertMalformedShapeIsCaught(source, new GrainMustBePartialAnalyzer(), "EDICT001");
    }

    static void AssertMalformedShapeIsCaught(string source, DiagnosticAnalyzer analyzer, string expectedDiagnosticId)
    {
        var diagnostics = AnalyzerTestHarness.Run(source, analyzer);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedDiagnosticId, diagnostic.Id);
    }
}
