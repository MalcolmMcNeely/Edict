using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using MessagePack;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Azure.Tests;

[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("AzureRecoverableOrders")]
public sealed partial record AzureRecoverableOrderPlacedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed class AzureRecoverableOrderRow
{
    public int OrderCount { get; set; }
}

public interface IAzureStreamPublisher : IGrainWithGuidKey
{
    Task PublishAsync(string streamName, EdictEvent evt);
}

public sealed class AzureStreamPublisher : Grain, IAzureStreamPublisher
{
    public Task PublishAsync(string streamName, EdictEvent evt) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(streamName, this.GetPrimaryKey()))
            .OnNextAsync(evt);
}

// Hand-written probe (Orleans codegen can see this, unlike the Edict-generated
// grain interface — ADR 0006) so the conformance test can read the pending
// outbox and drive the deterministic Reminder recovery drain.
public interface IAzureRecoverableProbe : IGrainWithGuidKey
{
    Task<int> GetPendingOutboxCountAsync();
    Task ForceDrainViaReminderAsync();
}

/// <summary>
/// Table projection whose row write is an UpsertRow outbox effect committed
/// atomically with the dedup-ring commit (ADR 0018). Backed by real Azure Table
/// Storage via Azurite; the conformance proof that ADR 0012's gap is closed.
/// </summary>
public sealed partial class AzureRecoverableOrderTableProjectionBuilder
    : EdictTableProjectionBuilder<AzureRecoverableOrderRow>, IAzureRecoverableProbe
{
    public AzureRecoverableOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azurerecoverableorderprojection";

    protected override string GetRowKey(EdictEvent evt) =>
        evt switch
        {
            AzureRecoverableOrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(AzureRecoverableOrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(((IOutboxHost)this).Outbox.Pending.Count);

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());
}
