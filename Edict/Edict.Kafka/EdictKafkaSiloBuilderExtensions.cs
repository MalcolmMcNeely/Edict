using Edict.Contracts.Configuration;
using Edict.Kafka.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streams;

namespace Edict.Kafka;

/// <summary>
/// Provider extension on <see cref="ISiloBuilder"/> for the Edict Kafka
/// streams provider. One call per consumer decision; chains the Orleans
/// <c>AddPersistentStreams</c> primitive plus the Edict topic-provisioner
/// hosted service plus the <see cref="EdictStreamsProviderMarker"/> the
/// startup validator inspects. Mirrors <c>AddEdictAzureStreams</c>'s shape so
/// a silo's <c>Program.cs</c> reads top-to-bottom as one Action lambda.
/// </summary>
public static class EdictKafkaSiloBuilderExtensions
{
    /// <summary>
    /// Registers the Edict Kafka stream provider, the topic provisioner,
    /// and the <see cref="EdictStreamsProviderMarker"/>.
    /// </summary>
    public static ISiloBuilder AddEdictKafkaStreams(
        this ISiloBuilder silo,
        Action<EdictKafkaStreamsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictKafkaStreamsOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            throw new InvalidOperationException(
                "AddEdictKafkaStreams requires EdictKafkaStreamsOptions.BootstrapServers.");
        }

        EdictKafkaContractFloors.ValidateProducerOverrides(options.ProducerConfigOverrides);
        EdictKafkaContractFloors.ValidateConsumerOverrides(options.ConsumerConfigOverrides);

        silo.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
        silo.Services.AddSingleton(options);
        silo.Services.AddSingleton(_ => EdictKafkaStreamRegistry.FromAppDomain());
        silo.Services.AddHostedService<EdictKafkaTopicProvisioner>();

        silo.AddPersistentStreams(options.StreamProviderName, EdictKafkaAdapterFactory.Create, builder =>
        {
            // The factory resolves the singleton EdictKafkaStreamsOptions
            // directly so PartitionCountByStream, AutoOffsetReset, Compression,
            // ReplicationFactor, and the raw Confluent.Kafka passthrough
            // dicts all reach the mapper and the receivers — no named-options
            // copy step that silently drops fields.
            builder.UseConsistentRingQueueBalancer();
            builder.Configure<StreamPullingAgentOptions>(ob => ob.Configure(o =>
            {
                o.BatchContainerBatchSize = 1;
                o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(100);
            }));
            builder.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(o => o.CacheSize = 1024));
            builder.Configure<StreamPubSubOptions>(ob => ob.Configure(o =>
                o.PubSubType = StreamPubSubType.ImplicitOnly));
        });

        return silo;
    }
}
