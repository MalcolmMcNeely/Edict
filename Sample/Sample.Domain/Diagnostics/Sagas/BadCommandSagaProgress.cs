using Edict.Contracts.Persistence;

namespace Sample.Domain.Diagnostics.Sagas;

public enum BadCommandSagaStage
{
    Started,
    Dispatched,
}

[GenerateSerializer]
[Alias("Sample.Silo.Diagnostics.BadCommandSagaProgress")]
public sealed class BadCommandSagaProgress : IEdictPersistedState
{
    [Id(0)]
    public BadCommandSagaStage Stage { get; set; } = BadCommandSagaStage.Started;
}
