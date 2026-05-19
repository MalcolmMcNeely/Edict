using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictCommandGeneratorTests
{
    private const string SampleConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Results;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed record CancelOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderGrain : EdictCommandHandlerGrain
        {
            public Task<EdictCommandResult> Handle(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictCommandResult> Handle(CancelOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;

    private const string TelemeterizedConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Results;
        using Edict.Contracts.Telemetry;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;

            [EdictTelemeterized]
            public string Sku { get; init; } = Sku;
        }

        public sealed record CancelOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderGrain : EdictCommandHandlerGrain
        {
            public Task<EdictCommandResult> Handle(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictCommandResult> Handle(CancelOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;

    [Fact]
    public Task Generator_emits_grain_interface_dispatch_and_AddEdict()
    {
        var generated = GeneratorTestHarness.Run(SampleConsumer);

        return Verify(generated);
    }

    [Fact]
    public Task Generator_emits_telemeterized_tag_writer_in_AddEdict()
    {
        var generated = GeneratorTestHarness.Run(TelemeterizedConsumer);

        return Verify(generated);
    }

    [Fact]
    public Task Generator_emits_alias_declaration_per_concrete_command()
    {
        var generated = GeneratorTestHarness.Run(SampleConsumer)
            .Where(kvp => kvp.Key.EndsWith(".Alias.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Verify(generated);
    }
}
