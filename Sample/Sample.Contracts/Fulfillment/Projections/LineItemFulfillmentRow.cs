using Edict.Contracts.Persistence;

using Sample.Contracts.Fulfillment.Domain;

namespace Sample.Contracts.Fulfillment.Projections;

/// <summary>
/// Read row for the per-line-item fulfillment projection. Partition key is
/// the <c>OrderId</c>; row key is the <c>LineItemId</c>. The Blazor
/// Fulfillment page renders these live as the grain-timer-driven
/// <c>FulfillmentCommandHandler</c> ticks each line to Fulfilled.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Contracts.Fulfillment.Projections.LineItemFulfillmentRow")]
public sealed class LineItemFulfillmentRow : IEdictPersistedState
{
    [Id(0)]
    public LineItemFulfillmentStatus Status { get; set; } = LineItemFulfillmentStatus.Pending;

    [Id(1)]
    public Guid LineItemId { get; set; }
}
