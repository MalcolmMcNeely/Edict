using Confluent.Kafka;

using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

using Orleans.Hosting;
using Orleans.TestingHost;

using Testcontainers.Kafka;

using Xunit;

namespace Edict.Spike.Kafka.Tests;

public sealed class CrashFixture : IAsyncLifetime
{
    KafkaContainer? _kafka;

    public string BootstrapServers { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder().Build();
        await _kafka.StartAsync();
        BootstrapServers = _kafka.GetBootstrapAddress().Replace("PLAINTEXT://", "");
    }

    public async Task DisposeAsync()
    {
        if (_kafka != null)
        {
            await _kafka.DisposeAsync();
        }
    }

    public async Task<TestCluster> DeployClusterAsync(string consumerGroup, string topic)
    {
        CrashSiloConfigurator.BootstrapServers = BootstrapServers;
        CrashSiloConfigurator.ConsumerGroup = consumerGroup;
        CrashSiloConfigurator.Topic = topic;

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.AddSiloBuilderConfigurator<CrashSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}

public sealed class CrashSiloConfigurator : ISiloConfigurator
{
    public static string BootstrapServers = "";
    public static string ConsumerGroup = "spike-edict-crash";
    public static string Topic = "spike-orders";

    public void Configure(ISiloBuilder silo)
    {
        silo.AddMemoryGrainStorageAsDefault();
        silo.AddMemoryGrainStorage("PubSubStore");
        silo.AddSpikeKafkaStreams(SpikeStreamNames.StreamProvider, o =>
        {
            o.BootstrapServers = BootstrapServers;
            o.Topic = Topic;
            o.PartitionCount = 4;
            o.ConsumerGroup = ConsumerGroup;
            o.AutoOffsetReset = AutoOffsetReset.Earliest;
        });
    }
}
