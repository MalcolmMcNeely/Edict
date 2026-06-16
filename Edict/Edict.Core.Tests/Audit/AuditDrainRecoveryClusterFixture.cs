using Edict.Contracts.Audit;
using Edict.Contracts.Sending;
using Edict.Core.Audit;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.Grains;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.Audit;

[CollectionDefinition(Name)]
public sealed class AuditDrainRecoveryCollection : ICollectionFixture<AuditDrainRecoveryClusterFixture>
{
    public const string Name = "AuditDrainRecovery";
}

// Fault-injecting stores the silo writes to and the test reads. Static because an
// Orleans test silo configurator is constructed by the runtime and cannot capture
// fixture instance state; the single test in this collection owns the toggles.
static class AuditDrainRecoveryStoreHolder
{
    public static readonly FaultInjectingAuditStore Instance = new();

    public static readonly FaultInjectingAuditPayloadStore PayloadInstance = new();
}

// In-memory cluster with auditing on and fault-injecting audit stores, so a test can
// fail the post-commit drain, deactivate the grain, and prove the staged record
// reaches the WORM store through on-activation recovery against a real grain, real
// reminder service, and real persistence.
public sealed class AuditDrainRecoveryClusterFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public FaultInjectingAuditStore AuditStore => AuditDrainRecoveryStoreHolder.Instance;

    public FaultInjectingAuditPayloadStore PayloadStore => AuditDrainRecoveryStoreHolder.PayloadInstance;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

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
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.Services.AddEdictAudit(_ => RecoveryPrincipal);
            siloBuilder.Services.AddSingleton<IEdictAuditStore>(AuditDrainRecoveryStoreHolder.Instance);
            siloBuilder.Services.AddSingleton<IEdictAuditPayloadStore>(AuditDrainRecoveryStoreHolder.PayloadInstance);
            siloBuilder.WithAudit();

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
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddEdictAudit(() => RecoveryPrincipal);
        }
    }

    public static readonly EdictPrincipal RecoveryPrincipal = EdictPrincipal.Of("recovery");
}
