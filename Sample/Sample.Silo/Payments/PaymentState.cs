using Edict.Contracts.Persistence;

using Sample.Contracts.Payments.Domain;

namespace Sample.Silo.Payments;

/// <summary>
/// Framework-owned durable aggregate state for a payment (ADR 0018). Persisted
/// grain state, so a frozen string-literal <c>[Alias]</c> survives a class
/// rename (ADR 0017); <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Payments.PaymentState")]
public sealed class PaymentState : IEdictPersistedState
{
    [Id(0)]
    public PaymentStatus Status { get; set; } = PaymentStatus.None;

    [Id(1)]
    public decimal Amount { get; set; }
}
