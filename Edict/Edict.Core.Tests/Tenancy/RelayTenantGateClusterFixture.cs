using Edict.Contracts.Configuration;
using Edict.Contracts.Tenancy;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tenancy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// In-memory cluster with tenancy on and a resolver that yields nobody (null): a
// relayed saga Dispatch must carry the tenant off the handled trigger event through
// the relay, never from the resolver. The real SendCommandExecutor stays in place so
// the relayed dispatch hits the composition-site gate; only the publish executor is
// swapped, for a table-free read of the dead-letter the gate's throw produces.
[CollectionDefinition(Name)]
public sealed class RelayTenantGateCollection : ICollectionFixture<RelayTenantGateClusterFixture>
{
    public const string Name = "RelayTenantGate";
}

public sealed class RelayTenantGateClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public IGrainFactory GrainFactory => Cluster.GrainFactory;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() =>
        Cluster is not null ? Cluster.DisposeAsync().AsTask() : Task.CompletedTask;

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(RelayTenantGateSaga).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.Services.AddEdictTenant(_ => (EdictTenantId?)null);
            // Promote on the first failed attempt so the gate's refusal dead-letters
            // promptly rather than waiting out the default exponential backoff.
            siloBuilder.Services.Configure<EdictOptions>(options => options.OutboxMaxAttempts = 1);

            var publishDescriptor = siloBuilder.Services.Single(descriptor =>
                descriptor.ServiceType == typeof(IOutboxEffectExecutor)
                && descriptor.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publishDescriptor);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, RelayGateDeadLetterCapturingExecutor>();

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddEdictTenant(() => (EdictTenantId?)null);
        }
    }
}
