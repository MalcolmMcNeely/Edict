using Edict.Contracts.Persistence;

namespace FixtureLibrary.WithSubmitOrder.Orders;

[GenerateSerializer]
[Alias("FixtureLibrary.WithSubmitOrder.Orders.OrderState")]
public sealed class OrderState : IEdictPersistedState
{
    [Id(0)]
    public bool IsSubmitted { get; set; }
}
