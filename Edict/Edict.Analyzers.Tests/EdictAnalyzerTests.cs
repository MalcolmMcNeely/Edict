using Edict.Analyzers;
using Xunit;

namespace Edict.Analyzers.Tests;

public class EdictAnalyzerTests
{
    // ── Shared source fragments ──────────────────────────────────────────────

    // A minimal well-formed command + grain used by several valid-case tests.
    private const string ValidBase = """
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

    // ── EDICT001: grain must be partial ─────────────────────────────────────

    [Fact]
    public void EDICT001_ShouldNotRaise_WhenGrainIsPartial()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new GrainMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT001_ShouldRaiseOnClassIdentifier_WhenGrainIsNotPartial()
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
            public class OrderCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT001", d.Id);
        Assert.Contains("OrderCommandHandler", d.GetMessage());
        // Line 10 (0-indexed): "public class OrderCommandHandler : EdictCommandHandler"
        Assert.Equal(10, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT001_ShouldNotRaise_WhenProjectionBuilderGrainIsPartial()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
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

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT001_ShouldRaiseOnClassIdentifier_WhenProjectionBuilderGrainIsNotPartial()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            using Edict.Core.Projections;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public class OrderProjectionBuilder : EdictProjectionBuilder
            {
                public Task Handle(OrderPlacedEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT001", d.Id);
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 12 (0-indexed): "public class OrderProjectionBuilder : EdictProjectionBuilder"
        Assert.Equal(12, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT002: Handle must return Task<EdictCommandResult> ────────────────────

    [Fact]
    public void EDICT002_ShouldNotRaise_WhenHandleReturnsTaskOfEdictCommandResult()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new HandleReturnTypeAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT002_ShouldRaiseOnMethod_WhenHandleReturnsWrongType()
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
                public Task<bool> Handle(PlaceOrder c) =>
                    Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleReturnTypeAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT002", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        Assert.Contains("OrderCommandHandler", d.GetMessage());
        // Line 12 (0-indexed): "public Task<bool> Handle(PlaceOrder c) =>"
        Assert.Equal(12, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT003: [EdictRouteKey] must be exactly one Guid property ──────────────

    [Fact]
    public void EDICT003_ShouldNotRaise_WhenCommandHasOneGuidRouteKey()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new RouteKeyAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnClass_WhenCommandHasNoRouteKey()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        // Line 3 (0-indexed): "public sealed record PlaceOrder..."
        Assert.Equal(3, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnEachProperty_WhenCommandHasMultipleRouteKeys()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId, Guid CorrelationId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [EdictRouteKey]
                public Guid CorrelationId { get; init; } = CorrelationId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("EDICT003", d.Id));
        Assert.All(diagnostics, d => Assert.Contains("PlaceOrder", d.GetMessage()));
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnProperty_WhenRouteKeyIsNotGuid()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(string OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public string OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderId", d.GetMessage());
        // Line 5 (0-indexed): "[EdictRouteKey]"  — attribute usage; property identifier on line 6
        Assert.Equal(6, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT003_ShouldNotRaise_WhenEventHasOneGuidRouteKey()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnClass_WhenEventHasNoRouteKey()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        // Line 4 (0-indexed): "public sealed partial record OrderPlacedEvent..."
        Assert.Equal(4, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT003_ShouldRaiseOnProperty_WhenEventRouteKeyIsNotGuid()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(string OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public string OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderId", d.GetMessage());
        // Line 8 (0-indexed): "public string OrderId { get; init; } = OrderId;"
        Assert.Equal(8, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT004: each command must route to exactly one grain ───────────────

    [Fact]
    public void EDICT004_ShouldNotRaise_WhenEachCommandHandledByOneGrain()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new DuplicateCommandRouteAnalyzer());

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
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
            public partial class DuplicateOrderCommandHandler : EdictCommandHandler
            {
                public Task<EdictCommandResult> Handle(PlaceOrder c) =>
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

    // ── EDICT006: concrete Command must be declared partial ─────────────────

    [Fact]
    public void EDICT006_ShouldNotRaise_WhenCommandIsPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new CommandMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT006_ShouldRaiseOnType_WhenConcreteCommandIsNotPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new CommandMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT006", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        // Line 3 (0-indexed): "public sealed record PlaceOrder..."
        Assert.Equal(3, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT006_ShouldNotRaise_WhenAbstractCommandSubtype()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            namespace Sample;
            public abstract record OrderCommand(Guid OrderId) : EdictCommand;
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new CommandMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    // ── EDICT007: concrete Event must be declared partial ───────────────────

    [Fact]
    public void EDICT007_ShouldNotRaise_WhenEventIsPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT007_ShouldNotRaise_WhenAbstractEventSubtype()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            namespace Sample;
            public abstract record OrderEvent(Guid OrderId) : EdictEvent;
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT007_ShouldRaiseOnType_WhenConcreteEventIsNotPartial()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT007", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        // Line 5 (0-indexed): "public sealed record OrderPlacedEvent..."
        Assert.Equal(5, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT008: concrete Event must declare [Stream] ──────────────────────

    [Fact]
    public void EDICT008_ShouldNotRaise_WhenEventHasStreamAttribute()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustHaveStreamAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT008_ShouldNotRaise_WhenAbstractEventSubtype()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            namespace Sample;
            public abstract record OrderEvent(Guid OrderId) : EdictEvent;
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustHaveStreamAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT008_ShouldRaiseOnType_WhenEventHasNoStreamAttribute()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Events;
            namespace Sample;
            public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new EventMustHaveStreamAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT008", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        // Line 4 (0-indexed): "public sealed partial record OrderPlacedEvent..."
        Assert.Equal(4, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT009: Projection Builder Handle must return Task with Event param ─

    [Fact]
    public void EDICT009_ShouldNotRaise_WhenProjectionHandleReturnsTaskWithEventParam()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
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

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenProjectionHandleReturnsWrongType()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Commands;
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
                public Task<bool> Handle(OrderPlacedEvent e) => Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("OrderPlacedEvent", d.GetMessage());
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 14 (0-indexed): "public Task<bool> Handle(OrderPlacedEvent e) => Task.FromResult(true);"
        Assert.Equal(14, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT009_ShouldRaiseOnMethod_WhenProjectionHandleParamIsNotEvent()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Contracts.Events;
            using Edict.Core.Projections;
            namespace Sample;
            public class NotAnEvent { }
            public partial class OrderProjectionBuilder : EdictProjectionBuilder
            {
                public Task Handle(NotAnEvent e) => Task.CompletedTask;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new ProjectionHandleSignatureAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT009", d.Id);
        Assert.Contains("NotAnEvent", d.GetMessage());
        Assert.Contains("OrderProjectionBuilder", d.GetMessage());
        // Line 8 (0-indexed): "public Task Handle(NotAnEvent e) => Task.CompletedTask;"
        Assert.Equal(8, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT005: [EdictTelemeterized] must be on a primitive property ────────────

    [Fact]
    public void EDICT005_ShouldNotRaise_WhenTelemeterizedIsOnPrimitivePropertyOfEvent()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId, string Sku) : EdictEvent
            {
                [EdictTelemeterized]
                public string Sku { get; init; } = Sku;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT005_ShouldRaiseOnProperty_WhenTelemeterizedIsOnNonPrimitiveEventProperty()
    {
        const string source = """
            using System;
            using Edict.Contracts.Events;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            public class OrderDetails { public string Info { get; set; } = ""; }
            [EdictStream("Orders")]
            public sealed partial record OrderPlacedEvent(Guid OrderId, OrderDetails Details) : EdictEvent
            {
                [EdictTelemeterized]
                public OrderDetails Details { get; init; } = Details;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT005", d.Id);
        Assert.Contains("Details", d.GetMessage());
        // Line 9 (0-indexed): "public OrderDetails Details { get; init; } = Details;"
        Assert.Equal(9, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void EDICT005_ShouldNotRaise_WhenTelemeterizedIsOnPrimitiveProperty()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [EdictTelemeterized]
                public string Sku { get; init; } = Sku;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT005_ShouldRaiseOnProperty_WhenTelemeterizedIsOnNonPrimitive()
    {
        const string source = """
            using System;
            using Edict.Contracts.Commands;
            using Edict.Contracts.Telemetry;
            namespace Sample;
            public class OrderDetails { public string Info { get; set; } = ""; }
            public sealed record PlaceOrder(Guid OrderId, OrderDetails Details) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [EdictTelemeterized]
                public OrderDetails Details { get; init; } = Details;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT005", d.Id);
        Assert.Contains("Details", d.GetMessage());
        // Line 10 (0-indexed): "public OrderDetails Details { get; init; } = Details;"
        Assert.Equal(10, d.Location.GetLineSpan().StartLinePosition.Line);
    }
}
