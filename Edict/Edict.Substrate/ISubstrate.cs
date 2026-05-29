using Edict.Contracts.TableStorage;

using Orleans.Hosting;

namespace Edict.Substrate;

/// <summary>
/// Brings up a substrate (Azurite today; Kafka/Postgres tomorrow) and hands
/// the harness back a runtime that knows how to wire a silo and a client at it.
/// The seam is intentionally tiny so adding a new substrate is a Testcontainers-up
/// + DI-callback job — no harness or runner changes (ADR-0030).
/// </summary>
public interface ISubstrate
{
    string Name { get; }

    Task<ISubstrateRuntime> StartAsync(CancellationToken cancellationToken, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop);
}

/// <summary>
/// Selects how a substrate brings up its runtime. Closed-loop is the existing
/// per-send latency sweep; saturation is the fire-and-forget count-at-window-end
/// pass introduced for the headline EPS table. Substrates without a meaningful
/// distinction (Azurite — Azure Queue streams have no offset-reset analogue) treat
/// the value as a hint and may no-op.
/// </summary>
public enum SubstrateStartMode
{
    ClosedLoop,
    Saturation,
}

public interface ISubstrateRuntime : IAsyncDisposable
{
    Action<ISiloBuilder> ConfigureSilo { get; }

    Action<IClientBuilder> ConfigureClient { get; }

    /// <summary>
    /// Builds a substrate-correct <see cref="IEdictTableRepository{T}"/> for a
    /// workload-specific row type. The substrate stays workload-free — it does
    /// not know <typeparamref name="TRow"/> — but it owns the choice of which
    /// concrete repo to instantiate against the table name the caller provides.
    /// Lets the throughput harness register one row repository per workload
    /// without branching on the active substrate.
    /// </summary>
    IEdictTableRepository<TRow> CreateRowRepository<TRow>(IServiceProvider serviceProvider, string tableName)
        where TRow : class, new();
}
