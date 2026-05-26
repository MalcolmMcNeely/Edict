using Edict.Contracts.Sending;

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
}
