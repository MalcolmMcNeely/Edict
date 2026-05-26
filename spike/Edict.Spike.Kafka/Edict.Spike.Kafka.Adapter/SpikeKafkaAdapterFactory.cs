using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikeKafkaAdapterFactory : IQueueAdapterFactory
{
    readonly string _name;
    readonly SpikeKafkaStreamOptions _options;
    readonly SpikePartitionMapper _mapper;
    readonly SpikePreCriterionLog _log;
    readonly ILoggerFactory _loggerFactory;
    readonly IQueueAdapterCache _cache;
    SpikeKafkaAdapter? _adapter;

    public SpikeKafkaAdapterFactory(
        string name,
        SpikeKafkaStreamOptions options,
        SimpleQueueCacheOptions cacheOptions,
        SpikePreCriterionLog log,
        ILoggerFactory loggerFactory)
    {
        _name = name;
        _options = options;
        _log = log;
        _loggerFactory = loggerFactory;
        _mapper = new SpikePartitionMapper(options.PartitionCount);
        _cache = new SimpleQueueAdapterCache(cacheOptions, name, loggerFactory);
    }

    public Task<IQueueAdapter> CreateAdapter()
    {
        _adapter ??= new SpikeKafkaAdapter(_name, _options, _mapper, _log, _loggerFactory);
        return Task.FromResult<IQueueAdapter>(_adapter);
    }

    public IQueueAdapterCache GetQueueAdapterCache() => _cache;

    public IStreamQueueMapper GetStreamQueueMapper() => _mapper;

    public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        => Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());

    public static SpikeKafkaAdapterFactory Create(IServiceProvider services, string name)
    {
        var options = services.GetRequiredService<IOptionsMonitor<SpikeKafkaStreamOptions>>().Get(name);
        var cacheOptions = services.GetRequiredService<IOptionsMonitor<SimpleQueueCacheOptions>>().Get(name);
        var log = services.GetRequiredService<SpikePreCriterionLog>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        return new SpikeKafkaAdapterFactory(name, options, cacheOptions, log, loggerFactory);
    }
}
