using Edict.Core.Projections;
using FixtureLibrary.Orders;

namespace FixtureLibrary.Activity;

public sealed partial class OrderActivityProjection : EdictProjectionBuilder
{
    public System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) =>
        System.Threading.Tasks.Task.CompletedTask;
}
