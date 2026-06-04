using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Cart.Events;
using Sample.Contracts.Cart.Projections;

namespace Sample.Domain.Cart.ProjectionBuilders;

public sealed partial class CheckedOutCartTableProjectionBuilder : EdictTableProjectionBuilder<CheckedOutCartRow>
{
    public CheckedOutCartTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "checkedoutcarts";

    protected override string GetRowKey(EdictEvent edictEvent) => "cart";

    public Task HandleAsync(CartCheckedOutEvent edictEvent)
    {
        CurrentRow.ItemCount = edictEvent.ItemCount;
        CurrentRow.Skus = string.Join(",", edictEvent.Skus);
        return Task.CompletedTask;
    }
}
