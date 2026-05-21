using Edict.Contracts.Persistence;

using Sample.Contracts.Fulfillment.Domain;

namespace Sample.Silo.Fulfillment.State;

/// <summary>
/// Framework-owned durable aggregate state for an order's fulfillment workflow.
/// Persisted grain state, so a frozen string-literal <c>[Alias]</c> survives a
/// class rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Fulfillment.FulfillmentState")]
public sealed class FulfillmentState : IEdictPersistedState
{
    [Id(0)]
    public Guid OrderId { get; set; }

    [Id(1)]
    public List<FulfillmentLine> Lines { get; set; } = [];
}

/// <summary>A single line being fulfilled. Part of the persisted aggregate state.</summary>
[GenerateSerializer]
[Alias("Sample.Silo.Fulfillment.FulfillmentLine")]
public sealed record FulfillmentLine
{
    [Id(0)]
    public Guid LineItemId { get; init; }

    [Id(1)]
    public LineItemFulfillmentStatus Status { get; init; }
}
