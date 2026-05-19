namespace Sample.Silo.Payments;

/// <summary>Stage the OrderPayment saga has durably reached.</summary>
public enum OrderPaymentStage
{
    Started,
    PaymentRequested,
    Confirmed,
    Compensated,
}

/// <summary>
/// Durable progress for the OrderPayment saga. Persisted inside the saga's
/// idempotency envelope, so a frozen string-literal <c>[Alias]</c> survives a
/// class rename (ADR 0017); <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Payments.OrderPaymentProgress")]
public sealed class OrderPaymentProgress
{
    [Id(0)]
    public OrderPaymentStage Stage { get; set; } = OrderPaymentStage.Started;
}
