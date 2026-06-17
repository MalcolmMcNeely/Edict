using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Projections;
using Edict.Tests.Conformance.Reactivation;

using Orleans;

namespace Edict.Tests.Conformance.Projections;

// A real in-grain State-species projection (EdictProjectionBuilder<TProjection>)
// carrying actual payload state, distinct from the EdictUnit-closed
// OrderProjectionBuilder which holds its count in a plain field. The projection
// ACCUMULATES the event's amount into its durable payload rather than mirroring a
// running total carried on the event, so a same-EventId redelivery that slipped
// past the dedup ring would over-count — making idempotent re-apply a real assertion
// at the projection level rather than one the event shape makes true for free.

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Projections.RunningTotalProjection")]
public sealed class RunningTotalProjection : IEdictPersistedState
{
    [Id(0)]
    public int Total { get; set; }
}

[EdictStream("StateProjectionTotals")]
public sealed partial record TotalIncrementedEvent(Guid CounterId, int Amount) : EdictEvent
{
    [EdictRouteKey]
    public Guid CounterId { get; init; } = CounterId;

    public int Amount { get; init; } = Amount;
}

public interface IRunningTotalProjectionProbe : IConfirmsDeactivation
{
    Task<int> GetTotalAsync();
}

public sealed partial class RunningTotalProjectionBuilder
    : EdictProjectionBuilder<RunningTotalProjection>, IRunningTotalProjectionProbe
{
    Guid _activationId;

    // Fresh per activation so the deactivate-and-confirm seam can poll until it
    // changes to confirm the grain genuinely came back from durable storage.
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _activationId = Guid.NewGuid();
        await base.OnActivateAsync(cancellationToken);
    }

    Task HandleAsync(TotalIncrementedEvent edictEvent)
    {
        Projection.Total += edictEvent.Amount;
        return Task.CompletedTask;
    }

    public Task<int> GetTotalAsync() => Task.FromResult(Projection.Total);

    public Task<Guid> GetActivationIdAsync() => Task.FromResult(_activationId);

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}
