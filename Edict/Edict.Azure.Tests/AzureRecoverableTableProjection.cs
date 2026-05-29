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

namespace Edict.Azure.Tests;

[EdictStream("AzureRecoverableOrders")]
public sealed partial record AzureRecoverableOrderPlacedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[GenerateSerializer]
[Alias("Edict.Azure.Tests.AzureRecoverableOrderRow")]
public sealed class AzureRecoverableOrderRow : IEdictPersistedState
{
    [Id(0)]
    public int OrderCount { get; set; }
}

public interface IAzureStreamPublisher : IGrainWithGuidKey
{
    Task PublishAsync(string streamName, EdictEvent edictEvent);
}

public sealed class AzureStreamPublisher : Grain, IAzureStreamPublisher
{
    public Task PublishAsync(string streamName, EdictEvent edictEvent) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(streamName, this.GetPrimaryKey()))
            .OnNextAsync(edictEvent);
}

// Hand-written probe — Orleans codegen can see this, unlike the
// Edict-generated grain interface.
public interface IAzureRecoverableProbe : IGrainWithGuidKey
{
    Task<int> GetPendingOutboxCountAsync();
    Task ForceDrainViaReminderAsync();
}

public sealed partial class AzureRecoverableOrderTableProjectionBuilder
    : EdictTableProjectionBuilder<AzureRecoverableOrderRow>, IAzureRecoverableProbe
{
    public AzureRecoverableOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "azurerecoverableorderprojection";

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            AzureRecoverableOrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(AzureRecoverableOrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(OutboxStateForProbe.Pending.Count);

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());
}
