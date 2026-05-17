using Edict.Generators;

namespace Edict.Core.Tests;

public class EdictAnalyzerTests
{
    // ── Shared source fragments ──────────────────────────────────────────────

    // A minimal well-formed command + grain used by several valid-case tests.
    private const string ValidBase = """
        using System;
        using System.Threading.Tasks;
        using Edict.Abstractions;
        using Edict.Core;
        namespace Sample;
        public sealed record PlaceOrder(Guid OrderId) : Command
        {
            [RouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        public partial class OrderGrain : CommandHandlerGrain
        {
            public Task<CommandResult> Handle(PlaceOrder c) =>
                Task.FromResult<CommandResult>(new CommandResult.Accepted());
        }
        """;

    // ── EDICT001: grain must be partial ─────────────────────────────────────

    [Fact]
    public void EDICT001_not_raised_when_grain_is_partial()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new GrainMustBePartialAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT001_raised_on_class_identifier_when_grain_is_not_partial()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Abstractions;
            using Edict.Core;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : Command
            {
                [RouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public class OrderGrain : CommandHandlerGrain
            {
                public Task<CommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<CommandResult>(new CommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new GrainMustBePartialAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT001", d.Id);
        Assert.Contains("OrderGrain", d.GetMessage());
        // Line 10 (0-indexed): "public class OrderGrain : CommandHandlerGrain"
        Assert.Equal(10, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT002: Handle must return Task<CommandResult> ────────────────────

    [Fact]
    public void EDICT002_not_raised_when_handle_returns_TaskOfCommandResult()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new HandleReturnTypeAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT002_raised_on_method_when_handle_returns_wrong_type()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Abstractions;
            using Edict.Core;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : Command
            {
                [RouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderGrain : CommandHandlerGrain
            {
                public Task<bool> Handle(PlaceOrder c) =>
                    Task.FromResult(true);
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new HandleReturnTypeAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT002", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        Assert.Contains("OrderGrain", d.GetMessage());
        // Line 12 (0-indexed): "public Task<bool> Handle(PlaceOrder c) =>"
        Assert.Equal(12, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT003: [RouteKey] must be exactly one Guid property ──────────────

    [Fact]
    public void EDICT003_not_raised_when_command_has_one_Guid_RouteKey()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new RouteKeyAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT003_raised_on_class_when_command_has_no_RouteKey()
    {
        const string source = """
            using System;
            using Edict.Abstractions;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : Command
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
    public void EDICT003_raised_on_each_property_when_command_has_multiple_RouteKeys()
    {
        const string source = """
            using System;
            using Edict.Abstractions;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId, Guid CorrelationId) : Command
            {
                [RouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [RouteKey]
                public Guid CorrelationId { get; init; } = CorrelationId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal("EDICT003", d.Id));
        Assert.All(diagnostics, d => Assert.Contains("PlaceOrder", d.GetMessage()));
    }

    [Fact]
    public void EDICT003_raised_on_property_when_RouteKey_is_not_Guid()
    {
        const string source = """
            using System;
            using Edict.Abstractions;
            namespace Sample;
            public sealed record PlaceOrder(string OrderId) : Command
            {
                [RouteKey]
                public string OrderId { get; init; } = OrderId;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new RouteKeyAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT003", d.Id);
        Assert.Contains("OrderId", d.GetMessage());
        // Line 5 (0-indexed): "[RouteKey]"  — attribute usage; property identifier on line 6
        Assert.Equal(6, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT004: each command must route to exactly one grain ───────────────

    [Fact]
    public void EDICT004_not_raised_when_each_command_handled_by_one_grain()
    {
        var diagnostics = AnalyzerTestHarness.Run(ValidBase, new DuplicateCommandRouteAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT004_raised_on_second_grain_Handle_when_command_is_duplicated()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Edict.Abstractions;
            using Edict.Core;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId) : Command
            {
                [RouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }
            public partial class OrderGrain : CommandHandlerGrain
            {
                public Task<CommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<CommandResult>(new CommandResult.Accepted());
            }
            public partial class DuplicateGrain : CommandHandlerGrain
            {
                public Task<CommandResult> Handle(PlaceOrder c) =>
                    Task.FromResult<CommandResult>(new CommandResult.Accepted());
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new DuplicateCommandRouteAnalyzer());

        var d = Assert.Single(diagnostics);
        Assert.Equal("EDICT004", d.Id);
        Assert.Contains("PlaceOrder", d.GetMessage());
        Assert.Contains("OrderGrain", d.GetMessage());
        // Line 17 (0-indexed): "public Task<CommandResult> Handle(PlaceOrder c) =>" in DuplicateGrain
        Assert.Equal(17, d.Location.GetLineSpan().StartLinePosition.Line);
    }

    // ── EDICT005: [Telemeterized] must be on a primitive property ────────────

    [Fact]
    public void EDICT005_not_raised_when_Telemeterized_is_on_primitive_property()
    {
        const string source = """
            using System;
            using Edict.Abstractions;
            namespace Sample;
            public sealed record PlaceOrder(Guid OrderId, string Sku) : Command
            {
                [RouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [Telemeterized]
                public string Sku { get; init; } = Sku;
            }
            """;

        var diagnostics = AnalyzerTestHarness.Run(source, new TelemeterizedMustBePrimitiveAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void EDICT005_raised_on_property_when_Telemeterized_is_on_non_primitive()
    {
        const string source = """
            using System;
            using Edict.Abstractions;
            namespace Sample;
            public class OrderDetails { public string Info { get; set; } = ""; }
            public sealed record PlaceOrder(Guid OrderId, OrderDetails Details) : Command
            {
                [RouteKey]
                public Guid OrderId { get; init; } = OrderId;
                [Telemeterized]
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
}
