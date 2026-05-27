using Microsoft.Extensions.Hosting;

using Orleans.Hosting;

using Xunit;

namespace Edict.Kafka.Tests.Configuration;

public sealed class EdictKafkaSiloBuilderExtensionsTests
{
    [Fact]
    public void AddEdictKafkaStreams_ShouldThrow_WhenProducerOverrideDowngradesAcks()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HostBuilder()
                .UseOrleans(silo => silo.AddEdictKafkaStreams(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.ProducerConfigOverrides["acks"] = "1";
                }))
                .Build());

        Assert.Contains("acks", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddEdictKafkaStreams_ShouldThrow_WhenProducerOverrideTurnsOffIdempotence()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HostBuilder()
                .UseOrleans(silo => silo.AddEdictKafkaStreams(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.ProducerConfigOverrides["enable.idempotence"] = "false";
                }))
                .Build());

        Assert.Contains("idempotence", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddEdictKafkaStreams_ShouldThrow_WhenConsumerOverrideTurnsOnAutoCommit()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HostBuilder()
                .UseOrleans(silo => silo.AddEdictKafkaStreams(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.ConsumerConfigOverrides["enable.auto.commit"] = "true";
                }))
                .Build());

        Assert.Contains("auto.commit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
