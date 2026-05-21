using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Outbox;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Core.Tests.Grains;

public interface IProjectionPublisherGrain : IGrainWithGuidKey
{
    Task PublishToStreamAsync(string streamName, EdictEvent evt);
}

/// <summary>
/// Test-only grain: publishes any event directly to a named domain stream,
/// bypassing EdictCommandHandler. Used by mechanism tests to inject events
/// with known EventIds.
/// </summary>
public sealed class ProjectionPublisherGrain : Grain, IProjectionPublisherGrain
{
    public Task PublishToStreamAsync(string streamName, EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(streamName, this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

/// <summary>Table row for the probed table-projection mechanism tests.</summary>
[GenerateSerializer]
[Alias("Edict.Core.Tests.Grains.ProbedOrderRow")]
public sealed class ProbedOrderRow : IEdictPersistedState
{
    [Id(0)]
    public int OrderCount { get; set; }
}

[EdictStream("ProbedOrders")]
public sealed partial record ProbedOrderPlacedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

// Hand-written probe interface (Orleans codegen can see this, unlike the
// Edict-generated grain interface — ADR 0006) so a test can read the
// outbox/ring state and force the Reminder recovery drain deterministically.
public interface IProbedTableProjection : IGrainWithGuidKey
{
    Task<int> GetPendingOutboxCountAsync();
    Task<bool> HasDrainReminderAsync();
    Task ForceDrainViaReminderAsync();
}

/// <summary>
/// Table projection whose row write is an UpsertRow outbox effect committed
/// atomically with the dedup-ring commit (ADR 0018). The probe surface proves
/// the row write is deferred (pending entry, no row) until the drain succeeds.
/// </summary>
public sealed partial class ProbedOrderTableProjectionBuilder
    : EdictTableProjectionBuilder<ProbedOrderRow>, IProbedTableProjection
{
    public ProbedOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "probedorderprojection";

    protected override string GetRowKey(EdictEvent evt) =>
        evt switch
        {
            ProbedOrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(ProbedOrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(OutboxStateForProbe.Pending.Count);

    public async Task<bool> HasDrainReminderAsync() =>
        await this.GetReminder("edict-outbox-drain") is not null;

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());
}
