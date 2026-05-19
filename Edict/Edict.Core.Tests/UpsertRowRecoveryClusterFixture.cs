using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.Outbox;
using Edict.Core.Tests.TableStorage;
using Edict.Generated;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests;

/// <summary>
/// In-memory cluster with a working <see cref="PublishEventExecutor"/> (so the
/// published event reaches the projection) and a flippable
/// <see cref="ControllableUpsertRowExecutor"/>, so a test can drive a crash
/// between the ring/outbox commit and the row write, then a recovery drain
/// (ADR 0016/0018 — the ADR-0012 gap closure). A real clock is used (Orleans'
/// memory-stream pulling agent reads <see cref="TimeProvider"/> from DI, so a
/// frozen clock would stall delivery); backoff is a tiny deterministic delay
/// instead of a virtual clock.
/// </summary>
public sealed class UpsertRowRecoveryClusterFixture : IAsyncLifetime
{
    static readonly InMemoryTableStoreFactory _tableStoreFactory = new();

    public TestCluster Cluster { get; private set; } = null!;

    public InMemoryTableStoreFactory TableStoreFactory => _tableStoreFactory;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (Cluster is not null)
            await Cluster.DisposeAsync();
    }

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
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton(new EdictOutboxOptions
            {
                // Tiny, deterministic backoff so the recovery drain is testable
                // in real time without a (delivery-stalling) frozen clock.
                BaseDelay = TimeSpan.FromMilliseconds(200),
                JitterFraction = 0,
            });
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(_tableStoreFactory);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, PublishEventExecutor>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ControllableUpsertRowExecutor>();
            siloBuilder.Services.AddSingleton<OutboxDrainEngine>();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-dedup");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

// Owns the process-wide ControllableUpsertRowExecutor static flag; its own
// serialised collection so no other collection races the flag.
[CollectionDefinition(Name)]
public sealed class UpsertRowRecoveryClusterCollection
    : ICollectionFixture<UpsertRowRecoveryClusterFixture>
{
    public const string Name = "UpsertRowRecoveryCluster";
}
