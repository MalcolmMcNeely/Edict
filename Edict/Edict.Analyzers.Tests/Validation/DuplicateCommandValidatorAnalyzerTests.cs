using Edict.Analyzers.Validation;

using Xunit;

namespace Edict.Analyzers.Tests.Validation;

public class DuplicateCommandValidatorAnalyzerTests
{
    [Fact]
    public void EDICT019_ShouldNotRaise_WhenEachCommandHasAtMostOneValidator()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed record CancelOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class PlaceOrderCommandValidator : EdictCommandValidator<PlaceOrder>
            {
            }
            public sealed class CancelOrderCommandValidator : EdictCommandValidator<CancelOrder>
            {
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new DuplicateCommandValidatorAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT019_ShouldNotRaise_WhenCompilationHasNoValidators()
    {
        var diagnostics = AnalyzerTestHarness.Run(AnalyzerTestSources.ValidBase, new DuplicateCommandValidatorAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT019_ShouldRaiseOnSecondValidator_WhenCommandIsValidatedTwice()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Core.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public sealed class FirstPlaceOrderCommandValidator : EdictCommandValidator<PlaceOrder>
            {
            }
            public sealed class SecondPlaceOrderCommandValidator : EdictCommandValidator<PlaceOrder>
            {
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new DuplicateCommandValidatorAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT019", diagnostic.Id);
        Assert.Contains("PlaceOrder", diagnostic.GetMessage());
        Assert.Contains("FirstPlaceOrderCommandValidator", diagnostic.GetMessage());
        // Line 12 (0-indexed): "public sealed class SecondPlaceOrderCommandValidator : EdictCommandValidator<PlaceOrder>"
        Assert.Equal(12, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }
}
