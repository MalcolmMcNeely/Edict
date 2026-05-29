using Edict.Core.Configuration;

using Microsoft.Extensions.Hosting;

using Orleans.Hosting;

using Xunit;

namespace Edict.Kafka.Tests.Configuration;

public sealed class EdictKafkaSiloBuilderExtensionsTests
{
    [Fact]
    public void AddEdictKafkaStreams_ShouldThrow_WhenProducerOverrideDowngradesAcks()
    {
        var exception = Assert.Throws<EdictWiringException>(() =>
            new HostBuilder()
                .UseOrleans(silo => silo.AddEdictKafkaStreams(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.ProducerConfigOverrides["acks"] = "1";
                }))
                .Build());

        Assert.Contains("acks", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddEdictKafkaStreams_ShouldThrow_WhenProducerOverrideTurnsOffIdempotence()
    {
        var exception = Assert.Throws<EdictWiringException>(() =>
            new HostBuilder()
                .UseOrleans(silo => silo.AddEdictKafkaStreams(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.ProducerConfigOverrides["enable.idempotence"] = "false";
                }))
                .Build());

        Assert.Contains("idempotence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddEdictKafkaStreams_ShouldThrow_WhenConsumerOverrideTurnsOnAutoCommit()
    {
        var exception = Assert.Throws<EdictWiringException>(() =>
            new HostBuilder()
                .UseOrleans(silo => silo.AddEdictKafkaStreams(o =>
                {
                    o.BootstrapServers = "localhost:9092";
                    o.ConsumerConfigOverrides["enable.auto.commit"] = "true";
                }))
                .Build());

        Assert.Contains("auto.commit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
