using Edict.Contracts.Persistence;

namespace Sample.Silo.Diagnostics.State;

[GenerateSerializer]
[Alias("Sample.Silo.Diagnostics.RejectingState")]
public sealed class RejectingState : IEdictPersistedState
{
    [Id(0)]
    public int RejectedCount { get; set; }
}
