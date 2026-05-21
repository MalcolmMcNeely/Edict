using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Azure.Tests.ClaimCheck;

// Stateful command-handler aggregate + raw stream capture grain + Edict event
// handler used by the ClaimCheck lift suite (issue #90). Its event rides the
// AzureClaimCheckCounters stream so the publish + receiver-unwrap paths can
// be observed end-to-end against the real Azure Queue + Azure Blob transport
// (ADR 0024, ADR 0029).

[GenerateSerializer]
[Alias("Edict.Azure.Tests.ClaimCheck.AzureClaimCheckCounterState")]
public sealed class AzureClaimCheckCounterState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record IncrementAzureClaimCheckCounterCommand(Guid CounterId, string Payload) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public string Payload { get; init; } = Payload;
}

[EdictStream("AzureClaimCheckCounters")]
public sealed partial record AzureClaimCheckCounterIncrementedEvent(Guid CounterId, int NewCount, string Payload) : EdictEvent
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int NewCount { get; init; } = NewCount;

    public string Payload { get; init; } = Payload;
}

public partial class AzureClaimCheckCounterAggregate : EdictCommandHandler<AzureClaimCheckCounterState>
{
    public Task<EdictCommandResult> Handle(IncrementAzureClaimCheckCounterCommand command)
    {
        State.Count++;
        Raise(new AzureClaimCheckCounterIncrementedEvent(command.CounterId, State.Count, command.Payload));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

// Raw-Grain capture: subscribes to the stream directly, bypassing the Edict
// consumer base — so it observes the on-the-wire shape (an EdictEventEnvelope
// when the publisher has taken the pointer branch). An EdictEventHandler
// would see the unwrapped inner event instead; that's the receiver-unwrap
// scenario, covered separately.
public interface IAzureClaimCheckEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("AzureClaimCheckCounters")]
public sealed class AzureClaimCheckEventCaptureGrain : Grain, IAzureClaimCheckEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureClaimCheckCounters", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}

public interface IAzureClaimCheckEventHandlerProbe : IGrainWithGuidKey
{
    Task<IReadOnlyList<AzureClaimCheckCounterIncrementedEvent>> GetHandledEventsAsync();
}

// EdictEventHandler routes through the framework's stream observer, which
// runs ClaimCheckUnwrap before dispatch — so Handle sees the inner event,
// not the pointer envelope. This is the receiver-side contract under test.
public sealed partial class AzureClaimCheckCounterEventHandler : EdictEventHandler, IAzureClaimCheckEventHandlerProbe
{
    readonly List<AzureClaimCheckCounterIncrementedEvent> _handled = [];

    public Task Handle(AzureClaimCheckCounterIncrementedEvent evt)
    {
        _handled.Add(evt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AzureClaimCheckCounterIncrementedEvent>> GetHandledEventsAsync() =>
        Task.FromResult<IReadOnlyList<AzureClaimCheckCounterIncrementedEvent>>(_handled.AsReadOnly());
}
