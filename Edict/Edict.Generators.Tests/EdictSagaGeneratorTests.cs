using static VerifyXunit.Verifier;

namespace Edict.Generators.Tests;

public class EdictSagaGeneratorTests
{
    const string SampleSaga = """
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

        public partial class OrderSaga : EdictSaga<OrderSagaProgress>
        {
            Task HandleAsync(OrderPlacedEvent edictEvent)
            {
                Progress.Placed = true;
                return Task.CompletedTask;
            }
        }
        """;

    const string SchedulingSaga = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Contracts.Schedules;
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

        public sealed partial record PollGateway : EdictScheduleMessage;

        public sealed class OrderSagaProgress
        {
            public bool Settled { get; set; }
        }

        public partial class OrderSaga : EdictSaga<OrderSagaProgress>
        {
            Task HandleAsync(OrderPlacedEvent edictEvent)
            {
                Schedule(new PollGateway(), every: TimeSpan.FromSeconds(2));
                return Task.CompletedTask;
            }

            Task<EdictScheduleResult> HandleAsync(PollGateway message) =>
                Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());
        }
        """;

    const string SchedulingSagaWithTimeout = """
        using System;
        using System.Threading.Tasks;

        using Edict.Contracts.Events;
        using Edict.Contracts.Schedules;
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

        public sealed partial record PollGateway : EdictScheduleMessage;

        public sealed class OrderSagaProgress
        {
            public bool Settled { get; set; }
        }

        public partial class OrderSaga : EdictSaga<OrderSagaProgress>
        {
            Task HandleAsync(OrderPlacedEvent edictEvent)
            {
                Schedule(new PollGateway(), every: TimeSpan.FromSeconds(2));
                return Task.CompletedTask;
            }

            Task<EdictScheduleResult> HandleAsync(PollGateway message) =>
                Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Continue());

            Task OnScheduleTimeoutAsync(PollGateway message) => Task.CompletedTask;
        }
        """;

    [Fact]
    public Task EdictSagaGenerator_ShouldEmitScheduleDispatch_WhenSagaHasScheduleArm()
    {
        var generated = GeneratorTestHarness.RunSagaGenerator(SchedulingSaga);

        return Verify(generated);
    }

    [Fact]
    public Task EdictSagaGenerator_ShouldEmitScheduleTimeoutDispatch_WhenSagaHasTimeoutArm()
    {
        var generated = GeneratorTestHarness.RunSagaGenerator(SchedulingSagaWithTimeout);

        return Verify(generated);
    }

    [Fact]
    public void EdictSagaGenerator_ShouldNotEmitScheduleDispatch_WhenSagaHasNoScheduleArm()
    {
        var generated = GeneratorTestHarness.RunSagaGenerator(SampleSaga);

        Assert.DoesNotContain(generated.Values, content => content.Contains("DispatchScheduleFireAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void EdictSagaGenerator_ShouldNotEmitScheduleTimeoutDispatch_WhenSagaHasNoTimeoutArm()
    {
        var generated = GeneratorTestHarness.RunSagaGenerator(SchedulingSaga);

        Assert.DoesNotContain(generated.Values, content => content.Contains("DispatchScheduleTimeoutAsync", StringComparison.Ordinal));
    }
}
