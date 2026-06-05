using FixtureLibrary.Orders;

namespace FixtureLibrary.Reporting;

public sealed partial class OrdersByStatusProjection : ReportingProjectionBase<OrdersByStatusRow>
{
    System.Threading.Tasks.Task HandleAsync(OrderPlaced edictEvent) =>
        System.Threading.Tasks.Task.CompletedTask;
}
