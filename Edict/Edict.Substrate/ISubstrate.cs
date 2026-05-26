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

    Task<ISubstrateRuntime> StartAsync(CancellationToken ct);
}

public interface ISubstrateRuntime : IAsyncDisposable
{
    Action<ISiloBuilder> ConfigureSilo { get; }

    Action<IClientBuilder> ConfigureClient { get; }
}
