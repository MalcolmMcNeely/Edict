using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// Orleans <see cref="IQueueAdapterFactory"/> for the Edict Kafka stream
/// provider. Resolved per stream-provider name from DI in
/// <see cref="EdictKafkaSiloBuilderExtensions.AddEdictKafkaStreams"/>.
/// </summary>
sealed class EdictKafkaAdapterFactory : IQueueAdapterFactory
{
    readonly string _name;
    readonly EdictKafkaStreamsOptions _options;
    readonly EdictKafkaPartitionMapper _mapper;
    readonly Serializer _serializer;
    readonly ILoggerFactory _loggerFactory;
    readonly IQueueAdapterCache _cache;

    EdictKafkaAdapter? _adapter;

    public EdictKafkaAdapterFactory(
        string name,
        EdictKafkaStreamsOptions options,
        SimpleQueueCacheOptions cacheOptions,
        Serializer serializer,
        ILoggerFactory loggerFactory,
        EdictKafkaStreamRegistry streamRegistry)
    {
        _name = name;
        _options = options;
        _serializer = serializer;
        _loggerFactory = loggerFactory;
        _mapper = new EdictKafkaPartitionMapper(options, streamRegistry);
        _cache = new SimpleQueueAdapterCache(cacheOptions, name, loggerFactory);
    }

    public Task<IQueueAdapter> CreateAdapter()
    {
        _adapter ??= new EdictKafkaAdapter(_name, _options, _mapper, _serializer, _loggerFactory);
        return Task.FromResult<IQueueAdapter>(_adapter);
    }

    public IQueueAdapterCache GetQueueAdapterCache() => _cache;

    public IStreamQueueMapper GetStreamQueueMapper() => _mapper;

    public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId) =>
        Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());

    public static EdictKafkaAdapterFactory Create(IServiceProvider services, string name)
    {
        var options = services.GetRequiredService<IOptionsMonitor<EdictKafkaStreamsOptions>>().Get(name);
        var cacheOptions = services.GetRequiredService<IOptionsMonitor<SimpleQueueCacheOptions>>().Get(name);
        var serializer = services.GetRequiredService<Serializer>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var streamRegistry = services.GetRequiredService<EdictKafkaStreamRegistry>();
        return new EdictKafkaAdapterFactory(name, options, cacheOptions, serializer, loggerFactory, streamRegistry);
    }
}
