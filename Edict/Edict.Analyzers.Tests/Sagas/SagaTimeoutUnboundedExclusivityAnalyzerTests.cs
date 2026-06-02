using Edict.Analyzers.Sagas;

using Xunit;

namespace Edict.Analyzers.Tests.Sagas;

public class SagaTimeoutUnboundedExclusivityAnalyzerTests
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
    public void EDICT021_ShouldRaise_WhenDurationAndUnboundedAreCombined()
    {
        var source = WithAttribute("""[EdictSagaTimeout("24:00:00", Unbounded = true)]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutUnboundedExclusivityAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT021", diagnostic.Id);
    }

    [Fact]
    public void EDICT021_ShouldNotRaise_WhenOnlyDurationIsSet()
    {
        var source = WithAttribute("""[EdictSagaTimeout("24:00:00")]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutUnboundedExclusivityAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT021_ShouldNotRaise_WhenOnlyUnboundedIsSet()
    {
        var source = WithAttribute("[EdictSagaTimeout(Unbounded = true)]");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutUnboundedExclusivityAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT021_ShouldNotRaise_WhenUnboundedIsExplicitlyFalseWithDuration()
    {
        var source = WithAttribute("""[EdictSagaTimeout("24:00:00", Unbounded = false)]""");

        var diagnostics = AnalyzerTestHarness.Run(source, new SagaTimeoutUnboundedExclusivityAnalyzer());

        Assert.Empty(diagnostics);
    }
}
