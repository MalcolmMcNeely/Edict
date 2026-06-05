using Edict.Contracts.Persistence;

namespace Sample.Domain.Countdown.State;

/// <summary>
/// Framework-owned durable aggregate state for a countdown. Persisted grain
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Countdown.CountdownState")]
public sealed class CountdownState : IEdictPersistedState
{
    [Id(0)]
    public Guid CountdownId { get; set; }

    [Id(1)]
    public int Remaining { get; set; }
}
