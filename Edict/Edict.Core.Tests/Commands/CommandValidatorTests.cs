using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Commands;

[Collection(SubstrateIndependentCollection.Name)]
public sealed class CommandValidatorTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public CommandValidatorTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Validator_ShouldReturnRejectedWithMappedReasons_WhenValidationFails()
    {
        var result = await _fixture.Sender.SendAsync(new CheckSkuCommand(Guid.NewGuid(), string.Empty));

        var rejected = Assert.IsType<EdictCommandResult.Rejected>(result);
        var reason = Assert.Single(rejected.Reasons);
        Assert.Equal("sku_required", reason.Code);
    }

    [Fact]
    public async Task Validator_ShouldAllowHandleToRunAndReturnAccepted_WhenValidationPasses()
    {
        var result = await _fixture.Sender.SendAsync(new CheckSkuCommand(Guid.NewGuid(), "SKU-1"));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task Handle_ShouldDispatchCommandNormally_WhenNoValidatorPresent()
    {
        var result = await _fixture.Sender.SendAsync(new PlaceOrderCommand(Guid.NewGuid(), "SKU-1"));

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
        await _fixture.Sender.SendAsync(new CheckSkuCommand(orderId, string.Empty));

        // A validation rejection is a normal return, not a fault, on both the Web
        // command span and the silo command-handle span that now carry the route key.
        var spans = stopped.Where(a => orderId.ToString("N").Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey))).ToList();
        Assert.NotEmpty(spans);
        Assert.All(spans, span => Assert.Equal(ActivityStatusCode.Unset, span.Status));
    }

    [Fact]
    public async Task Validator_ShouldReceiveGrainStateViaRootContextData()
    {
        // GrainStateRequiredValidator rejects on missing GrainState in
        // RootContextData; Accepted here proves the handler injected it.
        var result = await _fixture.Sender.SendAsync(new StateCheckCommand(Guid.NewGuid()));

        Assert.IsType<EdictCommandResult.Accepted>(result);
    }

    [Fact]
    public async Task ConcurrentCommands_ShouldCompleteWithoutInterleaving_WhenTargetingSameGrain()
    {
        var orderId = Guid.NewGuid();
        var firstSend = _fixture.Sender.SendAsync(new CheckSkuCommand(orderId, "SKU-A"));
        var secondSend = _fixture.Sender.SendAsync(new CheckSkuCommand(orderId, "SKU-B"));
        var results = await Task.WhenAll(firstSend, secondSend);

        Assert.All(results, result => Assert.IsType<EdictCommandResult.Accepted>(result));
    }
}
