using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Projections;

using Orleans;

namespace Edict.Core.Tests.Projections;

// A self-contained command → event → in-grain (State) projection on a dedicated
// stream, so the shared substrate-independent cluster's other consumers are
// untouched. The command raises an event carrying the running total; the in-grain
// projection mirrors it into the grain's own durable payload, committing inline
// with the dedup ring (no external store, no outbox effect).

[GenerateSerializer]
[Alias("Edict.Core.Tests.Projections.TallyState")]
public sealed class TallyState : IEdictPersistedState
{
    [Id(0)]
    public int Total { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Projections.TallyProjection")]
public sealed class TallyProjection : IEdictPersistedState
{
    [Id(0)]
    public int Total { get; set; }
}

public sealed partial record RecordTallyCommand(Guid TallyId, int Amount) : EdictCommand
{
    [EdictRouteKey]
    public Guid TallyId { get; init; } = TallyId;

    public int Amount { get; init; } = Amount;
}

[EdictStream("InGrainTally")]
public sealed partial record TallyRecordedEvent(Guid TallyId, int Total) : EdictEvent
{
    [EdictRouteKey]
    public Guid TallyId { get; init; } = TallyId;

    public int Total { get; init; } = Total;
}

public partial class TallyCommandHandler : EdictCommandHandler<TallyState>
{
    Task<EdictCommandResult> HandleAsync(RecordTallyCommand command)
    {
        State.Total += command.Amount;
        Raise(new TallyRecordedEvent(command.TallyId, State.Total));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public sealed partial class TallyStateProjectionBuilder : EdictProjectionBuilder<TallyProjection>
{
    Task HandleAsync(TallyRecordedEvent edictEvent)
    {
        Projection.Total = edictEvent.Total;
        return Task.CompletedTask;
    }
}
