using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using Orleans;

namespace Edict.Tests.Conformance.ClaimCheck;

// A minimal command → event → event-handler workload dedicated to the
// claim-check threshold-boundary scenario. Kept separate from the counter
// workload so no saga or projection rides the same stream — the only consumer is
// the probe handler, which reports back the framework-assigned EventId so the
// scenario can ask the real store whether a body was parked under it (pointer)
// or not (inline).

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.ClaimCheck.ThresholdProbeState")]
public sealed class ThresholdProbeState : IEdictPersistedState
{
}

public sealed partial record IncrementThresholdProbeCommand(Guid ProbeId, string Payload) : EdictCommand
{
    [EdictRouteKey]
    public Guid ProbeId { get; init; } = ProbeId;

    public string Payload { get; init; } = Payload;
}

[EdictStream("ConformanceClaimCheckThreshold")]
public sealed partial record ThresholdProbeEvent(Guid ProbeId, string Payload) : EdictEvent
{
    [EdictRouteKey]
    public Guid ProbeId { get; init; } = ProbeId;

    public string Payload { get; init; } = Payload;
}

public partial class ThresholdProbeAggregate : EdictCommandHandler<ThresholdProbeState>
{
    Task<EdictCommandResult> HandleAsync(IncrementThresholdProbeCommand command)
    {
        Raise(new ThresholdProbeEvent(command.ProbeId, command.Payload));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public interface IThresholdProbeHandler : IGrainWithGuidKey
{
    Task<Guid?> GetReceivedEventIdAsync();

    Task<DateTimeOffset?> GetReceivedOccurredAtAsync();
}

public sealed partial class ThresholdProbeEventHandler : EdictEventHandler, IThresholdProbeHandler
{
    Guid? _eventId;
    DateTimeOffset? _occurredAt;

    Task HandleAsync(ThresholdProbeEvent edictEvent)
    {
        _eventId = edictEvent.EventId;
        _occurredAt = edictEvent.OccurredAt;
        return Task.CompletedTask;
    }

    public Task<Guid?> GetReceivedEventIdAsync() => Task.FromResult(_eventId);

    public Task<DateTimeOffset?> GetReceivedOccurredAtAsync() => Task.FromResult(_occurredAt);
}

static class ClaimCheckThresholdWaiters
{
    // On a frozen-clock fixture the reference stream's pulling agent polls on that
    // clock, so a queued event never delivers until the clock moves. This nudges
    // the clock forward in small steps to pump delivery, polling the condition
    // between nudges; the nudge drives the agent, the wall-clock delay lets the
    // resulting async grain delivery settle. The threshold decision itself was made
    // at enqueue on the frozen epoch — this pump only drives delivery so the probe
    // handler can report the assigned EventId back.
    public static async Task PumpUntilAsync(Action<TimeSpan> advanceClock, Func<Task<bool>> condition, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            advanceClock(TimeSpan.FromMilliseconds(250));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
