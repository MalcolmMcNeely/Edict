using Edict.Analyzers.Sagas;

using Xunit;

namespace Edict.Analyzers.Tests.Sagas;

public class SagaTimeoutDurationAnalyzerTests
{
    const string SagaShell = """
        using System;
        using System.Threading.Tasks;
        using Edict.Contracts.Events;
        using Edict.Contracts.Persistence;
        using Edict.Contracts.Sagas;
        using Edict.Core.Sagas;
        namespace Sample;
        [EdictStream("Orders")]
        public sealed partial record OrderSubmitted(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        public sealed class OrderProgress : IEdictPersistedState
        {
        }
        {0}
        public partial class OrderSaga : EdictSaga<OrderProgress>
        {
            public Task HandleAsync(OrderSubmitted edictEvent) => Task.CompletedTask;
        }
        """;

    static string WithAttribute(string attribute) => SagaShell.Replace("{0}", attribute);

    [Fact]
    public void EDICT020_ShouldRaise_WhenDurationIsNotTimeSpanParseable()
    {
        var source = WithAttribute("""[EdictSagaTimeout("not-a-timespan")]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutDurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT020", diagnostic.Id);
    }

    [Fact]
    public void EDICT020_ShouldRaise_WhenDurationIsZero()
    {
        var source = WithAttribute("""[EdictSagaTimeout("00:00:00")]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutDurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT020", diagnostic.Id);
    }

    [Fact]
    public void EDICT020_ShouldRaise_WhenDurationIsNegative()
    {
        var source = WithAttribute("""[EdictSagaTimeout("-00:00:01")]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutDurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT020", diagnostic.Id);
    }

    [Fact]
    public void EDICT020_ShouldNotRaise_WhenDurationIsValidAndPositive()
    {
        var source = WithAttribute("""[EdictSagaTimeout("24:00:00")]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutDurationAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT020_ShouldNotRaise_WhenOnlyUnboundedIsSet()
    {
        var source = WithAttribute("[EdictSagaTimeout(Unbounded = true)]");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutDurationAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT020_ShouldNotRaise_WhenNoTimeoutAttributeIsPresent()
    {
        var source = WithAttribute("");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutDurationAnalyzer());

        Assert.Empty(diagnostics);
    }
}
