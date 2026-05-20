using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests.ClaimCheck;

/// <summary>
/// In-memory cluster wired for the receiver-side missing-blob dead-letter
/// loop (ADR 0024, slice 3). Real <see cref="TimeProvider.System"/> — the
/// memory-stream pulling agent reads <see cref="TimeProvider"/> from DI and a
/// frozen clock stalls delivery — paired with a tiny deterministic
/// <see cref="EdictOutboxOptions.BaseDelay"/> and a small
/// <see cref="EdictOutboxOptions.MaxAttempts"/> so the loop exhausts in well
/// under a second. The framework-shipped
/// <see cref="EdictDeadLetterProjectionBuilder"/> grain consumes the
/// <c>edict-dead-letter</c> stream and the auto-wired repository surfaces the
/// row to the caller.
/// </summary>
public sealed class BlobMissingDeadLetterClusterFixture : IAsyncLifetime
{
    static readonly InMemoryTableStoreFactory _tableStoreFactory = new();
    static readonly InMemoryClaimCheckStore _claimCheckStore = new();

    public TestCluster Cluster { get; private set; } = null!;

    public InMemoryTableStoreFactory TableStoreFactory => _tableStoreFactory;
    public InMemoryClaimCheckStore ClaimCheckStore => _claimCheckStore;

    public IEdictSender Sender => Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
    public IEdictDeadLetterRepository DeadLetterRepository =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictDeadLetterRepository>();

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
        {
            await Cluster.DisposeAsync();
        }
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
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(_tableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(_claimCheckStore);
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                _tableStoreFactory.GetOrCreateStore<EdictDeadLetterEntry>(
                    EdictDeadLetterProjectionBuilder.DeadLetterPartition));
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox(o =>
            {
                // Tight retry loop so the test exhausts in well under a second
                // without forcing a virtual clock that would stall the
                // memory-stream pulling agent.
                o.BaseDelay = TimeSpan.FromMilliseconds(30);
                o.MaxDelay = TimeSpan.FromMilliseconds(60);
                o.JitterFraction = 0;
                o.MaxAttempts = 3;
            });
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
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
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                _tableStoreFactory.GetOrCreateStore<EdictDeadLetterEntry>(
                    EdictDeadLetterProjectionBuilder.DeadLetterPartition));
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class BlobMissingDeadLetterClusterCollection
    : ICollectionFixture<BlobMissingDeadLetterClusterFixture>
{
    public const string Name = "BlobMissingDeadLetterCluster";
}
