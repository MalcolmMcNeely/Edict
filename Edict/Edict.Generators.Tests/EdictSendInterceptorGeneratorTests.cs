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
            public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }

        public sealed class Caller
        {
            public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId) =>
                sender.SendAsync(new PlaceOrder(orderId));
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
            public Task<EdictCommandResult> HandleAsync(PlaceOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictCommandResult> HandleAsync(CancelOrder c) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }

        public sealed class Caller
        {
            public async Task Use(IEdictSender sender, Guid orderId)
            {
                await sender.SendAsync(new PlaceOrder(orderId, "SKU-1"));
                await sender.SendAsync(new PlaceOrder(orderId, "SKU-2"));
                await sender.SendAsync(new CancelOrder(orderId));
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

    [Fact]
    public void EdictSendInterceptorGenerator_ShouldEmitNoInterceptor_WhenSendAsyncIsOnAnUnrelatedType()
    {
        // Keys on IEdictSender, not the method name. Intercepting a consumer's
        // own SendAsync would inject a broken stub with no error surfaced.
        const string consumer = """
            using System;
            using System.Threading.Tasks;

            using Edict.Contracts.Commands;

            namespace Sample;

            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            public sealed class NotASender
            {
                public Task SendAsync(EdictCommand command) => Task.CompletedTask;
            }

            public sealed class Caller
            {
                public Task Use(NotASender notASender, Guid orderId) =>
                    notASender.SendAsync(new PlaceOrder(orderId));
            }
            """;

        var generated = GeneratorTestHarness.RunSendInterceptorGenerator(consumer);

        Assert.DoesNotContain(generated, file => file.Key.EndsWith(".SendInterceptor.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void EdictSendInterceptorGenerator_ShouldEmitNoInterceptor_WhenArgumentIsBaseTyped()
    {
        // A base-typed argument has no concrete command type to intercept on,
        // so the site must fall through to the registrar. EDICT015 warns the
        // consumer; the generator must agree by emitting nothing.
        const string consumer = """
            using System;
            using System.Threading.Tasks;

            using Edict.Contracts.Commands;
            using Edict.Contracts.Sending;

            namespace Sample;

            public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
            {
                [EdictRouteKey]
                public Guid OrderId { get; init; } = OrderId;
            }

            public sealed class Caller
            {
                public Task<EdictCommandResult> Use(IEdictSender sender, Guid orderId)
                {
                    EdictCommand command = new PlaceOrder(orderId);
                    return sender.SendAsync(command);
                }
            }
            """;

        var generated = GeneratorTestHarness.RunSendInterceptorGenerator(consumer);

        Assert.DoesNotContain(generated, file => file.Key.EndsWith(".SendInterceptor.g.cs", StringComparison.Ordinal));
    }
}
