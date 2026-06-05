using Edict.Contracts.Persistence;

namespace Sample.Domain.Watchdog.State;

/// <summary>
/// Framework-owned durable aggregate state for a watchdog. Persisted grain state,
/// so a frozen string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Watchdog.WatchdogState")]
public sealed class WatchdogState : IEdictPersistedState
{
    [Id(0)]
    public Guid WatchdogId { get; set; }

    [Id(1)]
    public int Polls { get; set; }
}
