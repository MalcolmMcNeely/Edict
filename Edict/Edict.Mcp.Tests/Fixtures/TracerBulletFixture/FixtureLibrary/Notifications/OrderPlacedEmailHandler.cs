using Edict.Core.EventHandler;
using FixtureLibrary.Orders;

namespace FixtureLibrary.Notifications;

public sealed partial class OrderPlacedEmailHandler : EdictEventHandler
{
    public System.Threading.Tasks.Task Handle(OrderPlaced edictEvent) =>
        System.Threading.Tasks.Task.CompletedTask;
}
