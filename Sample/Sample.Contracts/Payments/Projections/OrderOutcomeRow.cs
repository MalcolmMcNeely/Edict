namespace Sample.Contracts.Payments.Projections;

/// <summary>Read model for the terminal outcome of the OrderPayment workflow.</summary>
public sealed class OrderOutcomeRow
{
    public string Outcome { get; set; } = "Pending";
}
