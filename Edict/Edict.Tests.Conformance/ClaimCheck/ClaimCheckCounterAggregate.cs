using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Telemetry;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.ClaimCheck;

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.ClaimCheck.ClaimCheckCounterState")]
public sealed class ClaimCheckCounterState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}

public sealed partial record IncrementClaimCheckCounterCommand(Guid CounterId, string Payload) : EdictCommand
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public string Payload { get; init; } = Payload;
}

[EdictStream("ConformanceClaimCheckCounters")]
public sealed partial record ClaimCheckCounterIncrementedEvent(Guid CounterId, int NewCount, string Payload) : EdictEvent
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int NewCount { get; init; } = NewCount;

    public string Payload { get; init; } = Payload;
}

public partial class ClaimCheckCounterAggregate : EdictCommandHandler<ClaimCheckCounterState>
{
    public Task<EdictCommandResult> HandleAsync(IncrementClaimCheckCounterCommand command)
    {
        State.Count++;
        Raise(new ClaimCheckCounterIncrementedEvent(command.CounterId, State.Count, command.Payload));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

// Raw-Grain capture: subscribes directly, bypassing the Edict consumer base,
// so it observes the on-the-wire shape (an EdictEventEnvelope on the pointer
// branch) rather than the unwrapped inner event an EdictEventHandler sees.
public interface IClaimCheckEventCaptureGrain : IGrainWithGuidKey
{
    Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync();
}

[ImplicitStreamSubscription("ConformanceClaimCheckCounters")]
public sealed class ClaimCheckEventCaptureGrain : Grain, IClaimCheckEventCaptureGrain
{
    readonly List<EdictEvent> _events = [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceClaimCheckCounters", this.GetPrimaryKey()));
        await stream.SubscribeAsync(
            (item, _) => { _events.Add(item); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EdictEvent>> GetCapturedEventsAsync() =>
        Task.FromResult<IReadOnlyList<EdictEvent>>(_events.AsReadOnly());
}

public interface IClaimCheckEventHandlerProbe : IGrainWithGuidKey
{
    Task<IReadOnlyList<ClaimCheckCounterIncrementedEvent>> GetHandledEventsAsync();
}

// Handle sees the inner event: EdictEventHandler routes through the
// framework's stream observer, which runs ClaimCheckUnwrap before dispatch.
public sealed partial class ClaimCheckCounterEventHandler : EdictEventHandler, IClaimCheckEventHandlerProbe
{
    readonly List<ClaimCheckCounterIncrementedEvent> _handled = [];

    public Task HandleAsync(ClaimCheckCounterIncrementedEvent edictEvent)
    {
        _handled.Add(edictEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClaimCheckCounterIncrementedEvent>> GetHandledEventsAsync() =>
        Task.FromResult<IReadOnlyList<ClaimCheckCounterIncrementedEvent>>(_handled.AsReadOnly());
}
