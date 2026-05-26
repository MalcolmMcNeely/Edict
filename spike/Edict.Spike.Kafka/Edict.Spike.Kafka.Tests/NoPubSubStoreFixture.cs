using Confluent.Kafka;

using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

using Orleans.Hosting;
using Orleans.Streams;
using Orleans.TestingHost;

using Testcontainers.Kafka;

using Xunit;

namespace Edict.Spike.Kafka.Tests;

public sealed class NoPubSubStoreFixture : IAsyncLifetime
{
    KafkaContainer? _kafka;
    TestCluster? _cluster;

    public string BootstrapServers { get; private set; } = "";
    public IClusterClient Client => _cluster!.Client;

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder().Build();
        await _kafka.StartAsync();
        BootstrapServers = _kafka.GetBootstrapAddress().Replace("PLAINTEXT://", "");

        NoPubSubStoreSiloConfigurator.BootstrapServers = BootstrapServers;
        NoPubSubStoreSiloConfigurator.Topic = $"spike-orders-{Guid.NewGuid():N}";
        NoPubSubStoreSiloConfigurator.ConsumerGroup = $"spike-nopubsub-{Guid.NewGuid():N}";

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<NoPubSubStoreSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
        if (_kafka != null)
        {
            await _kafka.DisposeAsync();
        }
    }
}

public sealed class NoPubSubStoreSiloConfigurator : ISiloConfigurator
{
    public static string BootstrapServers = "";
    public static string ConsumerGroup = "spike-edict-nopubsub";
    public static string Topic = "spike-orders";

    public void Configure(ISiloBuilder silo)
    {
        silo.AddMemoryGrainStorageAsDefault();
        // intentionally NOT adding AddMemoryGrainStorage("PubSubStore")
        silo.AddSpikeKafkaStreams(
            SpikeStreamNames.StreamProvider,
            o =>
            {
                o.BootstrapServers = BootstrapServers;
                o.Topic = Topic;
                o.PartitionCount = 4;
                o.ConsumerGroup = ConsumerGroup;
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            },
            pubSubType: StreamPubSubType.ImplicitOnly);
    }
}
