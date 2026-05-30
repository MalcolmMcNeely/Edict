using Edict.Core.Sagas;
using FixtureLibrary.Orders;

namespace FixtureLibrary.Shipping;

public sealed partial class ShipmentSaga : EdictSaga<ShipmentProgress>
{
    public System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) =>
        System.Threading.Tasks.Task.CompletedTask;
}
