using Confluent.Kafka;

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
sealed class EdictKafkaAdapterFactory : IQueueAdapterFactory, IDisposable
{
    readonly EdictKafkaPartitionMapper _mapper;
    readonly IQueueAdapterCache _cache;
    readonly Lazy<EdictKafkaAdapter> _adapter;

    public EdictKafkaAdapterFactory(
        string name,
        EdictKafkaStreamsOptions options,
        SimpleQueueCacheOptions cacheOptions,
        Serializer serializer,
        ILoggerFactory loggerFactory,
        EdictKafkaStreamRegistry streamRegistry)
        : this(name, options, cacheOptions, serializer, loggerFactory, streamRegistry, producerFactory: null)
    {
    }

    internal EdictKafkaAdapterFactory(
        string name,
        EdictKafkaStreamsOptions options,
        SimpleQueueCacheOptions cacheOptions,
        Serializer serializer,
        ILoggerFactory loggerFactory,
        EdictKafkaStreamRegistry streamRegistry,
        Func<EdictKafkaStreamsOptions, IProducer<string, byte[]>>? producerFactory)
    {
        _mapper = new EdictKafkaPartitionMapper(options, streamRegistry);
        _cache = new SimpleQueueAdapterCache(cacheOptions, name, loggerFactory);
        // Lazy with the default ExecutionAndPublication mode — a concurrent
        // CreateAdapter race would otherwise construct two EdictKafkaAdapter
        // instances and leak the losing one's IProducer (librdkafka background
        // threads + TCP connections).
        _adapter = new Lazy<EdictKafkaAdapter>(() =>
            new EdictKafkaAdapter(name, options, _mapper, serializer, loggerFactory, producerFactory));
    }

    public Task<IQueueAdapter> CreateAdapter() => Task.FromResult<IQueueAdapter>(_adapter.Value);

    // DI disposes the factory at silo shutdown; without this hop the
    // Lazy-held EdictKafkaAdapter (and the librdkafka IProducer it owns) only
    // releases on GC, which leaves background threads and TCP connections
    // pointing at the dead broker for the lifetime of the next silo in the
    // same process.
    public void Dispose()
    {
        if (_adapter.IsValueCreated)
        {
            _adapter.Value.Dispose();
        }
    }

    public IQueueAdapterCache GetQueueAdapterCache() => _cache;

    public IStreamQueueMapper GetStreamQueueMapper() => _mapper;

    public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId) =>
        Task.FromResult<IStreamFailureHandler>(new NoOpStreamDeliveryFailureHandler());

    public static EdictKafkaAdapterFactory Create(IServiceProvider services, string name)
    {
        // Resolve the singleton EdictKafkaStreamsOptions registered by
        // AddEdictKafkaStreams (the same instance the topic provisioner
        // injects), not the Orleans-named options instance — the named
        // path only carries the subset of fields the AddPersistentStreams
        // sub-builder copies, so PartitionCountByStream, AutoOffsetReset,
        // Compression, ReplicationFactor, and the raw passthrough dicts are
        // all lost on that path. Provisioner-vs-mapper agreement on partition
        // counts depends on resolving the same instance here.
        var options = services.GetRequiredService<EdictKafkaStreamsOptions>();
        var cacheOptions = services.GetRequiredService<IOptionsMonitor<SimpleQueueCacheOptions>>().Get(name);
        var serializer = services.GetRequiredService<Serializer>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        var streamRegistry = services.GetRequiredService<EdictKafkaStreamRegistry>();
        return new EdictKafkaAdapterFactory(name, options, cacheOptions, serializer, loggerFactory, streamRegistry);
    }
}
