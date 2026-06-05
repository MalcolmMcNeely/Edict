using Edict.Contracts.Persistence;

namespace Sample.Domain.Reservations.Sagas;

/// <summary>
/// Durable progress for the reservation saga. Persisted inside the saga's
/// idempotency envelope, so a frozen string-literal <c>[Alias]</c> survives a
/// class rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Reservations.ReservationProgress")]
public sealed class ReservationProgress : IEdictPersistedState
{
    [Id(0)]
    public Guid OrderId { get; set; }
}
