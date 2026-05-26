using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance;

/// <summary>
/// Substrate-agnostic surface every provider's conformance fixture exposes.
/// Concrete subclasses (e.g. <c>AzureClusterFixture</c>) own bringing up the
/// substrate-specific TestCluster and registering the conformance workload
/// assembly with the silo's serializer + DI. The shared base lets every
/// abstract scenario class accept any provider's fixture by type parameter.
/// </summary>
public abstract class ConformanceFixture : IAsyncLifetime
{
    public abstract IEdictSender Sender { get; }

    public abstract IGrainFactory GrainFactory { get; }

    public abstract Task InitializeAsync();

    public abstract Task DisposeAsync();

    /// <summary>
    /// Provider-bound read seam used by table-projection conformance scenarios
    /// to verify a row landed in the substrate's durable store after a
    /// projection write. Each substrate fixture returns its own
    /// <see cref="IEdictTableRepository{T}"/> implementation (e.g. Azure Table,
    /// Postgres) — the scenario stays substrate-neutral.
    /// </summary>
    public abstract IEdictTableRepository<T> GetTableRepository<T>(string tableName) where T : class, new();

    /// <summary>
    /// Provider-bound write seam used by table-backed conformance scenarios to
    /// seed rows before exercising a read contract. Mirrors the silo-side
    /// <see cref="IEdictTableStoreFactory"/> registration, but constructed
    /// against the test process so seeding does not need to round-trip through
    /// the cluster.
    /// </summary>
    public abstract IEdictTableStoreFactory TableStoreFactory { get; }
}
