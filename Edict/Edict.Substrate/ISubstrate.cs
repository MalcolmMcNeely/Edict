using Edict.Contracts.TableStorage;

using Orleans.Hosting;

namespace Edict.Substrate;

/// <summary>
/// Brings up a substrate (Azurite today; Kafka/Postgres tomorrow) and hands
/// the harness back a runtime that knows how to wire a silo and a client at it.
/// The seam is intentionally tiny so adding a new substrate is a Testcontainers-up
/// + DI-callback job — no harness or runner changes.
/// </summary>
public interface ISubstrate
{
    string Name { get; }

    Task<ISubstrateRuntime> StartAsync(CancellationToken cancellationToken, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop);
}

/// <summary>
/// Selects how a substrate brings up its runtime. Closed-loop is the per-send
/// latency sweep; the two saturation modes are the fire-and-forget
/// count-at-window-end passes behind the headline EPS table, one per projection
/// species — <see cref="SaturationList"/> measures the external List counter,
/// <see cref="SaturationState"/> the in-grain State counter, so the table can show
/// the storage-commit cost side by side. Both saturation modes share the same
/// fresh-consumer-group offset-reset need; substrates without a meaningful
/// distinction (Azurite — Azure Queue streams have no offset-reset analogue) treat
/// the value as a hint and may no-op.
/// </summary>
public enum SubstrateStartMode
{
    ClosedLoop,
    SaturationList,
    SaturationState,
}

public interface ISubstrateRuntime : IAsyncDisposable
{
    Action<ISiloBuilder> ConfigureSilo { get; }

    Action<IClientBuilder> ConfigureClient { get; }

    /// <summary>
    /// Builds a substrate-correct <see cref="IEdictTableWriteStore{T}"/> for a
    /// workload-specific row type. The substrate stays workload-free — it does
    /// not know <typeparamref name="TRow"/> — but it owns the choice of which
    /// concrete store to instantiate against the table name the caller provides.
    /// Lets the throughput harness read rows store-direct (off-grain, the right
    /// shape for a latency measurement) without branching on the active substrate.
    /// </summary>
    IEdictTableWriteStore<TRow> CreateRowStore<TRow>(IServiceProvider serviceProvider, string tableName)
        where TRow : class, new();
}
