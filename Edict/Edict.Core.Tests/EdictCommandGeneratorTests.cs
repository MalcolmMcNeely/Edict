using static VerifyXunit.Verifier;

namespace Edict.Core.Tests;

public class EdictCommandGeneratorTests
{
    private const string SampleConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Abstractions;
        using Edict.Core;

        namespace Sample;

        public sealed record PlaceOrder(Guid OrderId, string Sku) : Command
        {
            [RouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed record CancelOrder(Guid OrderId) : Command
        {
            [RouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderGrain : CommandHandlerGrain
        {
            public Task<CommandResult> Handle(PlaceOrder command) =>
                Task.FromResult<CommandResult>(new CommandResult.Accepted());

            public Task<CommandResult> Handle(CancelOrder command) =>
                Task.FromResult<CommandResult>(new CommandResult.Accepted());
        }
        """;

    [Fact]
    public Task Generator_emits_grain_interface_dispatch_and_AddEdict()
    {
        var generated = GeneratorTestHarness.Run(SampleConsumer);

        return Verify(generated);
    }
}
