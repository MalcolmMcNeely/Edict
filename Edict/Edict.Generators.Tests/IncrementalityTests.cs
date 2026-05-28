using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Edict.Generators.Tests;

public class IncrementalityTests
{
    [Fact]
    public void Harness_DetectsBrokenGenerator_ThatSubscribesDirectlyToCompilationProvider()
    {
        const string anySource = "namespace Sample { internal sealed class Placeholder { } }";

        Assert.Throws<GeneratorIncrementalityException>(() =>
            GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<BrokenViaCompilationProviderGenerator>(anySource));
    }

    [Fact]
    public void EdictCommandGenerator_RemainsCached_OnUnrelatedEdit()
    {
        GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<EdictCommandGenerator>(CommandSource);
    }

    [Fact]
    public void EdictEventGenerator_RemainsCached_OnUnrelatedEdit()
    {
        GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<EdictEventGenerator>(EventSource);
    }

    [Fact]
    public void EdictEventHandlerGenerator_RemainsCached_OnUnrelatedEdit()
    {
        GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<EdictEventHandlerGenerator>(EventHandlerSource);
    }

    [Fact]
    public void EdictEventStreamAccessorsGenerator_RemainsCached_OnUnrelatedEdit()
    {
        GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<EdictEventStreamAccessorsGenerator>(EventSource);
    }

    [Fact]
    public void EdictProjectionGenerator_RemainsCached_OnUnrelatedEdit()
    {
        GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<EdictProjectionGenerator>(ProjectionSource);
    }

    [Fact]
    public void EdictSagaGenerator_RemainsCached_OnUnrelatedEdit()
    {
        GeneratorIncrementalityHarness.AssertCachedOnUnrelatedEdit<EdictSagaGenerator>(SagaSource);
    }

    private const string CommandSource = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> Handle(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;

    private const string EventSource = """
        using System;

        using Edict.Contracts.Events;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        """;

    private const string EventHandlerSource = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.EventHandler;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial class OrderEmailHandler : EdictEventHandler
        {
            public Task Handle(OrderPlacedEvent evt) => Task.CompletedTask;
        }
        """;

    private const string ProjectionSource = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Projections;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder
        {
            public Task Handle(OrderPlacedEvent evt) => Task.CompletedTask;
        }
        """;

    private const string SagaSource = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Core.Sagas;
        using MessagePack;

        namespace Sample;

        [MessagePackObject(keyAsPropertyName: true)]
        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed class OrderSagaProgress
        {
            public bool Placed { get; set; }
        }

        public sealed partial class OrderSaga : EdictSaga<OrderSagaProgress>
        {
            public Task Handle(OrderPlacedEvent evt)
            {
                Progress.Placed = true;
                return Task.CompletedTask;
            }
        }
        """;

    /// <summary>
    /// Anti-pattern: subscribing <c>RegisterSourceOutput</c> directly to
    /// <see cref="IncrementalGeneratorInitializationContext.CompilationProvider"/>.
    /// The Compilation reference changes on every edit (even a comment in an
    /// unrelated tree), so the output step re-runs every time. This is the
    /// canonical "closes over Compilation" mistake the harness must catch.
    /// </summary>
    private sealed class BrokenViaCompilationProviderGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.CompilationProvider,
                static (spc, compilation) =>
                    spc.AddSource(
                        "Broken.g.cs",
                        SourceText.From($"// {compilation.AssemblyName}", Encoding.UTF8)));
        }
    }
}
