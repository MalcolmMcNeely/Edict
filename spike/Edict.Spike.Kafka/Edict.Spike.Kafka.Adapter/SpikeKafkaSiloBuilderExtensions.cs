using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;

namespace Edict.Spike.Kafka.Adapter;

public static class SpikeKafkaSiloBuilderExtensions
{
    public static ISiloBuilder AddSpikeKafkaStreams(
        this ISiloBuilder silo,
        string providerName,
        Action<SpikeKafkaStreamOptions> configure)
    {
        silo.Services.TryAddSingleton<SpikePreCriterionLog>();
        silo.Services.AddHostedService<SpikeKafkaTopicProvisioner>();

        silo.AddPersistentStreams(providerName, SpikeKafkaAdapterFactory.Create, builder =>
        {
            builder.Configure<SpikeKafkaStreamOptions>(ob => ob.Configure(configure));
            builder.UseConsistentRingQueueBalancer();
            builder.Configure<StreamPullingAgentOptions>(ob => ob.Configure(o =>
            {
                o.BatchContainerBatchSize = 1;
                o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(100);
            }));
            builder.Configure<SimpleQueueCacheOptions>(ob => ob.Configure(o => o.CacheSize = 1024));
        });

        return silo;
    }
}
