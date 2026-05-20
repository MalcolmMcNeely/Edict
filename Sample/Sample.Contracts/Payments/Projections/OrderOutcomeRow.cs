using Edict.Contracts.Persistence;

namespace Sample.Contracts.Payments.Projections;

/// <summary>Read model for the terminal outcome of the OrderPayment workflow.</summary>
[GenerateSerializer]
[Alias("Sample.Contracts.Payments.Projections.OrderOutcomeRow")]
public sealed class OrderOutcomeRow : IEdictPersistedState
{
    [Id(0)]
    public string Outcome { get; set; } = "Pending";
}
