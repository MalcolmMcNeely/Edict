using Edict.Contracts.Persistence;

namespace Sample.Contracts.Delivery.Projections;

[GenerateSerializer]
[Alias("Sample.Contracts.Delivery.Projections.DeliveryStatusRow")]
public sealed class DeliveryStatusRow : IEdictPersistedState
{
    [Id(0)]
    public int EtaDaysRemaining { get; set; }

    [Id(1)]
    public bool Delivered { get; set; }
}
