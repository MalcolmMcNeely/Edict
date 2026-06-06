using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Delivery.Events;
using Sample.Contracts.Delivery.Projections;

namespace Sample.Domain.Delivery.ProjectionBuilders;

public sealed partial class DeliveryStatusProjectionBuilder : EdictListProjectionBuilder<DeliveryStatusRow>
{
    public DeliveryStatusProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "deliverystatus";

    protected override string GetRowKey(EdictEvent edictEvent) => "delivery";

    Task HandleAsync(DeliveryEtaTickedEvent edictEvent)
    {
        CurrentRow.EtaDaysRemaining = edictEvent.EtaDaysRemaining;
        return Task.CompletedTask;
    }

    Task HandleAsync(DeliveredEvent edictEvent)
    {
        CurrentRow.Delivered = true;
        return Task.CompletedTask;
    }
}
