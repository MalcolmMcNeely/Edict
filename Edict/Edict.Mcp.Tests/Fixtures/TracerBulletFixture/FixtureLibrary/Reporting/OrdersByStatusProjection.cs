using FixtureLibrary.Orders;

namespace FixtureLibrary.Reporting;

public sealed partial class OrdersByStatusProjection : ReportingTableProjectionBase<OrdersByStatusRow>
{
    public System.Threading.Tasks.Task Handle(OrderPlaced edictEvent) =>
        System.Threading.Tasks.Task.CompletedTask;
}
