using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.ClaimCheck;
using Edict.Core.Tests.Commands;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.TestSupport;

// One shared in-memory cluster for the substrate-independent conformance
// behaviours that used to be re-proven on every real-backend cross-fixture:
// command validation, the saga command-span parentage, dedup span emission, and
// the idempotency window-size config read. Nothing here touches a real stream or
// store, so the same observable behaviour is proven once, in-process — memory
// streams, memory grain storage, in-memory reminders. Real publish/send-command
// executors stay in place (the span and dedup behaviours need the live flow);
// activity propagation is on so a command span dispatched across a grain hop
// keeps the saga's trace.
[CollectionDefinition(Name)]
public sealed class SubstrateIndependentCollection : ICollectionFixture<SubstrateIndependentClusterFixture>
{
    public const string Name = "SubstrateIndependent";
}

public sealed class SubstrateIndependentClusterFixture : IAsyncLifetime
{
    public const int ConfiguredWindowSize = 2;

    public TestCluster Cluster { get; private set; } = null!;

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
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Validators (CheckSkuRequiredValidator, GrainStateRequiredValidator)
            // are picked up by AddEdict's AddValidatorsFromAssemblies scan.
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(new InMemoryClaimCheckStore());
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox(options => options.IdempotencyWindowSize = ConfiguredWindowSize);
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
