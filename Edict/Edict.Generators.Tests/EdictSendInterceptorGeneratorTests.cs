using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictSendInterceptorGeneratorTests
{
    const string SingleSendCallSiteConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Sending;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }

        public sealed class Caller
        {
            public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId) =>
                sender.Send(new PlaceOrder(orderId));
        }
        """;

    const string MultipleSendCallSitesConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Sending;
        using Edict.Contracts.Telemetry;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;

            [EdictTelemeterized]
            public string Sku { get; init; } = Sku;
        }

        public sealed partial record CancelOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> Handle(PlaceOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictCommandResult> Handle(CancelOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }

        public sealed class Caller
        {
            public async Task Use(IEdictSender sender, Guid orderId)
            {
                await sender.Send(new PlaceOrder(orderId, "SKU-1"));
                await sender.Send(new PlaceOrder(orderId, "SKU-2"));
                await sender.Send(new CancelOrder(orderId));
            }
        }
        """;

    [Fact]
    public Task EdictSendInterceptorGenerator_ShouldEmitInterceptorPerCommand_WithOneInterceptsLocationPerCallSite()
    {
        var generated = GeneratorTestHarness.RunSendInterceptorGenerator(SingleSendCallSiteConsumer);

        return Verify(generated);
    }

    [Fact]
    public Task EdictSendInterceptorGenerator_ShouldGroupMultipleCallSitesUnderOnePerTypeStub_AndCarryTelemeterizedTags()
    {
        var generated = GeneratorTestHarness.RunSendInterceptorGenerator(MultipleSendCallSitesConsumer);

        return Verify(generated);
    }

    [Fact]
    public void EdictSendInterceptorGenerator_ShouldEmitNothing_WhenInterceptorsDisabled()
    {
        var generated = GeneratorTestHarness.RunSendInterceptorGenerator(
            SingleSendCallSiteConsumer, interceptorsEnabled: false);

        Assert.Empty(generated);
    }
}
