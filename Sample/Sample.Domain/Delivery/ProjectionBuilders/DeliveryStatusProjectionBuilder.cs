using Edict.Core.Projections;

using Sample.Contracts.Delivery.Events;
using Sample.Contracts.Delivery.Projections;

namespace Sample.Domain.Delivery.ProjectionBuilders;

public sealed partial class DeliveryStatusProjectionBuilder : EdictProjectionBuilder<DeliveryStatusRow>
{
    Task HandleAsync(DeliveryEtaTickedEvent edictEvent)
    {
        Projection.EtaDaysRemaining = edictEvent.EtaDaysRemaining;
        return Task.CompletedTask;
    }

    Task HandleAsync(DeliveredEvent edictEvent)
    {
        Projection.Delivered = true;
        return Task.CompletedTask;
    }
}
