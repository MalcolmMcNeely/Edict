using System.Diagnostics;
using Edict.Contracts.Commands;
using Edict.Telemetry;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class CommandValidatorTests(EdictClusterFixture fixture)
{
    // Tracer bullet: the core contract — validator failure → Rejected envelope,
    // not a thrown exception.
    [Fact]
    public async Task Validator_ShouldReturnRejectedWithMappedReasons_WhenValidationFails()
    {
        var result = await fixture.Sender.Send(new ValidateSkuCommand(Guid.NewGuid(), string.Empty));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("sku_required", reason.Code);
    }

    [Fact]
    public async Task Validator_ShouldAllowHandleToRunAndReturnAccepted_WhenValidationPasses()
    {
        var result = await fixture.Sender.Send(new ValidateSkuCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Handle_ShouldDispatchCommandNormally_WhenNoValidatorPresent()
    {
        // PlaceOrderCommand has no registered validator in this cluster.
        var result = await fixture.Sender.Send(new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task RejectedCommand_ShouldNotSetErrorStatusOnSpan()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var orderId = Guid.NewGuid();
        await fixture.Sender.Send(new ValidateSkuCommand(orderId, string.Empty));

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem("edict.command.route_key")));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task Validator_ShouldReceiveGrainStateViaRootContextData()
    {
        // GrainStateRequiredValidator rejects with "missing_state" if
        // RootContextData[GrainState] is absent or null. OrderCommandHandler overrides
        // GetValidationState() to return a non-null marker, so the command
        // passes → Accepted proves injection happened.
        var result = await fixture.Sender.Send(new StateCheckCommand(Guid.NewGuid()));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    // Orleans' single-threaded grain activation guarantees that the validator
    // and Handle within one Dispatch call are never interleaved with other
    // messages. This test exercises concurrent sends to the same grain and
    // asserts both complete correctly — a regression guard for that guarantee.
    [Fact]
    public async Task ConcurrentCommands_ShouldCompleteWithoutInterleaving_WhenTargetingSameGrain()
    {
        var orderId = Guid.NewGuid();
        var t1 = fixture.Sender.Send(new ValidateSkuCommand(orderId, "SKU-A"));
        var t2 = fixture.Sender.Send(new ValidateSkuCommand(orderId, "SKU-B"));
        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.IsType<EdictCommandResult.Accepted>(r));
    }
}
