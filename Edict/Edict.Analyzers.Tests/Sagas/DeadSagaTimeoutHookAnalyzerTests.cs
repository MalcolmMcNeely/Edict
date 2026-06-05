using Edict.Analyzers.Sagas;

using Xunit;

namespace Edict.Analyzers.Tests.Sagas;

public class DeadSagaTimeoutHookAnalyzerTests
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
            Task HandleAsync(OrderSubmitted edictEvent) => Task.CompletedTask;
        {1}
        }
        """;

    static string Build(string attribute, string body) =>
        SagaShell.Replace("{0}", attribute).Replace("{1}", body);

    const string TimeoutOverride = """
            protected override Task OnSagaTimeoutAsync() => Task.CompletedTask;
        """;

    [Fact]
    public void EDICT022_ShouldRaise_WhenHookOverriddenOnExplicitlyUnboundedSaga()
    {
        var source = Build("[EdictSagaTimeout(Unbounded = true)]", TimeoutOverride);

        var diagnostics = AnalyzerTestHarness.Run(source, new DeadSagaTimeoutHookAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("EDICT022", diagnostic.Id);
    }

    [Fact]
    public void EDICT022_ShouldNotRaise_WhenUnboundedSagaDoesNotOverrideHook()
    {
        var source = Build("[EdictSagaTimeout(Unbounded = true)]", "");

        var diagnostics = AnalyzerTestHarness.Run(source, new DeadSagaTimeoutHookAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT022_ShouldNotRaise_WhenHookOverriddenOnFiniteCappedSaga()
    {
        var source = Build("""[EdictSagaTimeout("24:00:00")]""", TimeoutOverride);

        var diagnostics = AnalyzerTestHarness.Run(source, new DeadSagaTimeoutHookAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT022_ShouldNotRaise_WhenHookOverriddenWithNoTimeoutAttribute()
    {
        var source = Build("", TimeoutOverride);

        var diagnostics = AnalyzerTestHarness.Run(source, new DeadSagaTimeoutHookAnalyzer());

        Assert.Empty(diagnostics);
    }
}
