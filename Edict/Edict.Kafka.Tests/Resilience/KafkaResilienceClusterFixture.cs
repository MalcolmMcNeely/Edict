using Confluent.Kafka;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Internal;
using Edict.Tests.Conformance.Streaming.References;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Kafka;

using Xunit;

namespace Edict.Kafka.Tests.Resilience;

// Owns its own Kafka container instead of sharing KafkaAssemblyHost: pausing it
// mid-test would corrupt every parallel collection running against the shared
// broker. The broker is the fault point — persistence is the dumb in-memory
// reference (memory grain storage + the in-memory reference table/claim-check
// stores), so a paused broker cannot perturb it. Single silo matches the Kafka
// adapter's known-working shape.
public sealed class KafkaResilienceClusterFixture : IAsyncLifetime
{
    KafkaContainer _kafka = null!;
    string _bootstrapServers = "";
    string _consumerGroup = "";
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder().Build();
        await _kafka.StartAsync();

        var address = _kafka.GetBootstrapAddress();
        _bootstrapServers = address.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? address.Substring("PLAINTEXT://".Length)
            : address;
        _consumerGroup = $"edict-kafka-resilience-{Guid.NewGuid():N}";

        var context = new KafkaResilienceContext(
            _bootstrapServers, _consumerGroup, new ReferenceTableStoreFactory(), new ReferenceClaimCheckStore());
        _contextKey = KafkaResilienceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[KafkaResilienceContextRegistry.ContextKeyProperty] = _contextKey;
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
        KafkaResilienceContextRegistry.Unregister(_contextKey);
        if (_kafka is not null)
        {
            await _kafka.DisposeAsync();
        }
    }

    // Pause preserves the host port binding. Mirrors the Azurite resilience suite:
    // stop+start re-binds the host port, masking the framework's reconnect
    // behaviour behind a host-wiring artefact.
    public async Task PauseKafkaAsync() => await _kafka.PauseAsync();

    public async Task UnpauseKafkaAsync() => await _kafka.UnpauseAsync();

    // Tests call this on entry so the fixture starts from a known-good baseline
    // even if a previous test panicked mid pause.
    public async Task EnsureRunningAsync()
    {
        if (_kafka.State == DotNet.Testcontainers.Containers.TestcontainersStates.Paused)
        {
            await _kafka.UnpauseAsync();
        }
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(KafkaResilienceTestEvent).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[KafkaResilienceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = KafkaResilienceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            // AddEdictKafkaStreams contributes the streams marker; reference
            // persistence registers no provider, so the persistence marker is
            // supplied here to satisfy the startup wiring validator.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.BootstrapServers;
                o.ConsumerGroupId = ctx.ConsumerGroup;
                o.PartitionCount = 4;
                // Earliest so the receiver replays anything written before the
                // consumer-group member finished joining.
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            });
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KafkaResilienceCollection : ICollectionFixture<KafkaResilienceClusterFixture>
{
    public const string Name = "KafkaResilience";
}
