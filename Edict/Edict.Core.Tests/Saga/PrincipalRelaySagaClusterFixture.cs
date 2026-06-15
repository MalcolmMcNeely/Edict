using Edict.Contracts.Audit;
using Edict.Core;
using Edict.Core.Audit;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.Audit;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.Saga;

// An in-memory cluster with auditing on, so the origin fail-closed gate is live.
// The registered resolver yields nobody (null): a relayed saga Dispatch must carry
// the originating principal off the handled event through the relay, never from the
// resolver, and must pass the gate without throwing. Real publish/send-command
// executors stay in place so the dispatched command is read back off the receiving
// handler grain — the actor a consumer would see on the wire.
[CollectionDefinition(Name)]
public sealed class PrincipalRelaySagaCollection : ICollectionFixture<PrincipalRelaySagaClusterFixture>
{
    public const string Name = "PrincipalRelaySaga";
}

public sealed class PrincipalRelaySagaClusterFixture : IAsyncLifetime
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
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
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
            siloBuilder.Services.AddEdictAudit(_ => (EdictPrincipal?)null);
            siloBuilder.Services.AddSingleton<IEdictAuditStore>(new RecordingAuditStore());
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
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}
