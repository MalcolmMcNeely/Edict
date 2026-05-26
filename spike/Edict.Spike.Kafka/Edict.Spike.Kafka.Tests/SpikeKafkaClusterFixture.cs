using Confluent.Kafka;

using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

using Orleans.Hosting;
using Orleans.TestingHost;

using Testcontainers.Kafka;

using Xunit;

namespace Edict.Spike.Kafka.Tests;

public sealed class SpikeKafkaClusterFixture : IAsyncLifetime
{
    KafkaContainer? _kafka;
    TestCluster? _cluster;

    public string BootstrapServers { get; private set; } = "";
    public IClusterClient Client => _cluster!.Client;
    public TestCluster Cluster => _cluster!;

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder().Build();
        await _kafka.StartAsync();
        BootstrapServers = _kafka.GetBootstrapAddress().Replace("PLAINTEXT://", "");

        SpikeSiloConfigurator.BootstrapServers = BootstrapServers;

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<SpikeSiloConfigurator>();
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

public sealed class SpikeSiloConfigurator : ISiloConfigurator
{
    public static string BootstrapServers = "";
    public static string ConsumerGroup = "spike-edict-test";

    public void Configure(ISiloBuilder silo)
    {
        silo.AddMemoryGrainStorageAsDefault();
        silo.AddMemoryGrainStorage("PubSubStore");
        silo.AddSpikeKafkaStreams(SpikeStreamNames.StreamProvider, o =>
        {
            o.BootstrapServers = BootstrapServers;
            o.Topic = SpikeStreamNames.OrdersTopic;
            o.PartitionCount = 4;
            o.ConsumerGroup = ConsumerGroup;
            // Earliest in tests so a produce that lands before the consumer's
            // first poll isn't silently skipped by "latest"-resolution.
            o.AutoOffsetReset = AutoOffsetReset.Earliest;
        });
    }
}
