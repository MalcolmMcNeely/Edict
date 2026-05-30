using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictRaiseInterceptorGeneratorTests
{
    const string RaiseConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Events;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [EdictStream("Orders")]
        public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        [EdictStream("Orders")]
        public sealed partial record OrderCancelledEvent(Guid OrderId) : EdictEvent
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> HandleAsync(PlaceOrder c)
            {
                Raise(new OrderPlacedEvent(c.OrderId));
                Raise(new OrderCancelledEvent(c.OrderId));
                return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
        }
        """;

    [Fact]
    public Task EdictRaiseInterceptorGenerator_ShouldEmitInterceptorPerEventType()
    {
        var generated = GeneratorTestHarness.RunRaiseInterceptorGenerator(RaiseConsumer);

        return Verify(generated);
    }

    [Fact]
    public void EdictRaiseInterceptorGenerator_ShouldEmitNothing_WhenInterceptorsDisabled()
    {
        var generated = GeneratorTestHarness.RunRaiseInterceptorGenerator(
            RaiseConsumer, interceptorsEnabled: false);

        Assert.Empty(generated);
    }
}
