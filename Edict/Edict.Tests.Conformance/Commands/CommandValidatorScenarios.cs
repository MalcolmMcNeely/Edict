using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Telemetry;

using Xunit;

namespace Edict.Tests.Conformance.Commands;

public abstract class CommandValidatorScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected CommandValidatorScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Validator_ShouldReturnRejectedWithMappedReasons_WhenValidationFails()
    {
        var result = await _fixture.Sender.Send(new ValidateSkuCommand(Guid.NewGuid(), string.Empty));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("sku_required", reason.Code);
    }

    [Fact]
    public async Task Validator_ShouldAllowHandleToRunAndReturnAccepted_WhenValidationPasses()
    {
        var result = await _fixture.Sender.Send(new ValidateSkuCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Handle_ShouldDispatchCommandNormally_WhenNoValidatorPresent()
    {
        var result = await _fixture.Sender.Send(new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

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
        await _fixture.Sender.Send(new ValidateSkuCommand(orderId, string.Empty));

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem("edict.command.route_key")));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task Validator_ShouldReceiveGrainStateViaRootContextData()
    {
        // GrainStateRequiredValidator rejects on missing GrainState in
        // RootContextData; Accepted here proves the handler injected it.
        var result = await _fixture.Sender.Send(new StateCheckCommand(Guid.NewGuid()));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task ConcurrentCommands_ShouldCompleteWithoutInterleaving_WhenTargetingSameGrain()
    {
        var orderId = Guid.NewGuid();
        var t1 = _fixture.Sender.Send(new ValidateSkuCommand(orderId, "SKU-A"));
        var t2 = _fixture.Sender.Send(new ValidateSkuCommand(orderId, "SKU-B"));
        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.IsType<EdictCommandResult.Accepted>(r));
    }
}
