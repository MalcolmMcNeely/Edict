namespace Sample.Silo.Payments;

/// <summary>Lifecycle of a payment aggregate (one payment per order).</summary>
public enum PaymentStatus
{
    None,
    Authorized,
    Declined,
}
