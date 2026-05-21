using Edict.Contracts.Persistence;

namespace Sample.Silo.Diagnostics.State;

[GenerateSerializer]
[Alias("Sample.Silo.Diagnostics.SimulationState")]
public sealed class SimulationState : IEdictPersistedState
{
    [Id(0)]
    public bool Triggered { get; set; }
}
