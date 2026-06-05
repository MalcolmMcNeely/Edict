using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictCommandGeneratorTests
{
    const string SampleConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial record CancelOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictCommandResult> HandleAsync(CancelOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;

    const string TelemeterizedConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
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
            public Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictCommandResult> HandleAsync(CancelOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
        }
        """;

    const string StatefulConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId, string Sku) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed class OrderState
        {
            public string Status { get; set; } = "Open";
        }

        public partial class OrderCommandHandler : EdictCommandHandler<OrderState>
        {
            public Task<EdictCommandResult> HandleAsync(PlaceOrder command)
            {
                State.Status = "Placed";
                return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
            }
        }
        """;

    [Fact]
    public Task EdictCommandGenerator_ShouldEmitSpine_WhenHandlerDerivesFromGenericStatefulBase()
    {
        var generated = GeneratorTestHarness.Run(StatefulConsumer);

        return Verify(generated);
    }

    [Fact]
    public Task EdictCommandGenerator_ShouldEmitGrainInterfaceDispatchAndRouteRegistrar()
    {
        var generated = GeneratorTestHarness.Run(SampleConsumer);

        return Verify(generated);
    }

    [Fact]
    public Task EdictCommandGenerator_ShouldEmitTelemeterizedTagWriterInRouteRegistrar()
    {
        var generated = GeneratorTestHarness.Run(TelemeterizedConsumer);

        return Verify(generated);
    }

    [Fact]
    public Task EdictCommandGenerator_ShouldEmitAliasDeclarationPerConcreteCommand()
    {
        var generated = GeneratorTestHarness.Run(SampleConsumer)
            .Where(kvp => kvp.Key.EndsWith(".Alias.g.cs", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Verify(generated);
    }

    const string SchedulingConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Schedules;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial record FulfillNextLine : EdictScheduleMessage;

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictScheduleResult> HandleAsync(FulfillNextLine message) =>
                Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());
        }
        """;

    [Fact]
    public Task EdictCommandGenerator_ShouldEmitScheduleDispatch_WhenHandlerHasScheduleArm()
    {
        var generated = GeneratorTestHarness.Run(SchedulingConsumer);

        return Verify(generated);
    }

    [Fact]
    public void EdictCommandGenerator_ShouldNotEmitScheduleDispatch_WhenHandlerHasNoScheduleArm()
    {
        var generated = GeneratorTestHarness.Run(SampleConsumer);

        Assert.DoesNotContain(generated.Values, content => content.Contains("DispatchScheduleFireAsync", StringComparison.Ordinal));
    }

    const string SchedulingWithTimeoutConsumer = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Commands;
        using Edict.Contracts.Schedules;
        using Edict.Core.Commands;

        namespace Sample;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }

        public sealed partial record FulfillNextLine : EdictScheduleMessage;

        public partial class OrderCommandHandler : EdictCommandHandler
        {
            public Task<EdictCommandResult> HandleAsync(PlaceOrder command) =>
                Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());

            public Task<EdictScheduleResult> HandleAsync(FulfillNextLine message) =>
                Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());

            public Task OnScheduleTimeoutAsync(FulfillNextLine message) => Task.CompletedTask;
        }
        """;

    [Fact]
    public Task EdictCommandGenerator_ShouldEmitScheduleTimeoutDispatch_WhenHandlerHasTimeoutArm()
    {
        var generated = GeneratorTestHarness.Run(SchedulingWithTimeoutConsumer);

        return Verify(generated);
    }

    [Fact]
    public void EdictCommandGenerator_ShouldNotEmitScheduleTimeoutDispatch_WhenHandlerHasNoTimeoutArm()
    {
        var generated = GeneratorTestHarness.Run(SchedulingConsumer);

        Assert.DoesNotContain(generated.Values, content => content.Contains("DispatchScheduleTimeoutAsync", StringComparison.Ordinal));
    }

    const string ContractsOnlyConsumer = """
        using System;

        using Edict.Contracts.Commands;

        namespace Sample.Contracts;

        public sealed partial record PlaceOrder(Guid OrderId) : EdictCommand
        {
            [EdictRouteKey]
            public Guid OrderId { get; init; } = OrderId;
        }
        """;

    [Fact]
    public void EdictCommandGenerator_ShouldNotEmitRouteRegistrar_WhenAssemblyHasNoHandlers()
    {
        var generated = GeneratorTestHarness.Run(ContractsOnlyConsumer);

        Assert.DoesNotContain(generated.Keys, name => name.Contains("EdictRouteRegistrar", StringComparison.Ordinal));
        Assert.DoesNotContain(generated.Values, content => content.Contains("EdictRoutesAttribute", StringComparison.Ordinal));
    }
}
