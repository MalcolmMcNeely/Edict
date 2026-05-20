using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using MessagePack;

using Orleans;
using Orleans.Runtime;

namespace Edict.Core.Tests.Grains;

/// <summary>Table row for the probed table-projection mechanism tests.</summary>
public sealed class ProbedOrderRow
{
    public int OrderCount { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
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
