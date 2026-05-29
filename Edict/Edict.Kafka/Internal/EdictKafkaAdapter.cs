using Confluent.Kafka;

using Edict.Kafka.Wire;

using Microsoft.Extensions.Logging;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// Custom Orleans <see cref="IQueueAdapter"/> over <c>Confluent.Kafka</c>.
/// One producer instance per silo. Every event is produced to the topic named
/// after the <see cref="Contracts.Events.EdictStreamAttribute"/> domain stream
/// it belongs to (ADR-0028 §2) — the producer reads the stream namespace off
/// the Orleans <see cref="StreamId"/> the publish pipeline stamps, with no
/// shared central topic. Partition selection inside that topic is the
/// <see cref="EdictKafkaPartitionMapper"/>'s job so per-aggregate ordering is
/// preserved. Producer floors are hardcoded: <c>acks=all</c>, idempotent
/// producer on, lz4 compression — contract floors ADR-0028 records, not
/// consumer-tunable.
/// </summary>
sealed class EdictKafkaAdapter : IQueueAdapter, IDisposable
{
    readonly EdictKafkaStreamsOptions _options;
    readonly EdictKafkaPartitionMapper _mapper;
    readonly Serializer _serializer;
    readonly IProducer<string, byte[]> _producer;
    readonly ILogger<EdictKafkaAdapter> _logger;
    readonly ILoggerFactory _loggerFactory;
    readonly Func<EdictKafkaStreamsOptions, string, int, IConsumer<string, byte[]>>? _consumerFactory;

    // Receivers Orleans has handed out via CreateReceiver. Tracked so that
    // adapter Dispose can force-shutdown any consumer Orleans did not drain
    // — under saturation load Orleans' silo-shutdown budget can finish
    // before every PullingAgent has awaited its Receiver.Shutdown to
    // completion, leaving librdkafka background threads polling a dead
    // broker into the next test or benchmark iteration and stealing CPU on
    // small hosts.
    readonly object _receiversLock = new();
    readonly List<EdictKafkaReceiver> _receivers = [];

    public EdictKafkaAdapter(
        string name,
        EdictKafkaStreamsOptions options,
        EdictKafkaPartitionMapper mapper,
        Serializer serializer,
        ILoggerFactory loggerFactory,
        Func<EdictKafkaStreamsOptions, IProducer<string, byte[]>>? producerFactory = null,
        Func<EdictKafkaStreamsOptions, string, int, IConsumer<string, byte[]>>? consumerFactory = null)
    {
        Name = name;
        _options = options;
        _mapper = mapper;
        _serializer = serializer;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<EdictKafkaAdapter>();
        _consumerFactory = consumerFactory;

        _producer = (producerFactory ?? BuildDefaultProducer)(options);

        IProducer<string, byte[]> BuildDefaultProducer(EdictKafkaStreamsOptions o)
        {
            var producerConfig = EdictKafkaProducerConfigFactory.Build(o, clientId: $"{name}-producer");
            return new ProducerBuilder<string, byte[]>(producerConfig).Build();
        }
    }

    public string Name { get; }

    public bool IsRewindable => false;

    public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

    public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
    {
        var topic = EdictKafkaPartitionMapper.TopicFor(queueId);
        var partition = EdictKafkaPartitionMapper.PartitionFor(queueId);
        var consumerFactory = _consumerFactory is null
            ? (Func<IConsumer<string, byte[]>>?)null
            : () => _consumerFactory(_options, topic, partition);
        var receiver = new EdictKafkaReceiver(
            Name,
            partition,
            topic,
            _options,
            _serializer,
            _loggerFactory.CreateLogger<EdictKafkaReceiver>(),
            consumerFactory);
        lock (_receiversLock)
        {
            _receivers.Add(receiver);
        }
        return receiver;
    }

    public async Task QueueMessageBatchAsync<T>(
        StreamId streamId,
        IEnumerable<T> events,
        StreamSequenceToken token,
        Dictionary<string, object> requestContext)
    {
        var queueId = _mapper.GetQueueForStream(streamId);
        var partition = EdictKafkaPartitionMapper.PartitionFor(queueId);
        var keyString = streamId.GetKeyAsString() ?? streamId.ToString();
        var ns = streamId.GetNamespace() ?? string.Empty;

        var envelope = new EdictKafkaWireEnvelope
        {
            StreamNamespace = ns,
            StreamKey = keyString,
            Events = events.Cast<object>().ToArray(),
        };

        var body = _serializer.SerializeToArray(envelope);

        var message = new Message<string, byte[]> { Key = keyString, Value = body };
        var topicPartition = new TopicPartition(ns, new Partition(partition));
        var report = await _producer.ProduceAsync(topicPartition, message);
        _logger.LogDebug(
            "Edict.Kafka produced offset {Offset} to {Topic}-{Partition} key={Key} (n={Count})",
            report.Offset.Value, report.Topic, report.Partition.Value, keyString, envelope.Events.Length);
    }

    public void Dispose()
    {
        EdictKafkaReceiver[] receivers;
        lock (_receiversLock)
        {
            receivers = [.. _receivers];
            _receivers.Clear();
        }
        // Force-drain any receiver Orleans did not await to completion. Each
        // receiver tracks its own _shutdownInFlight, so a second Shutdown is
        // a no-op handoff to the in-flight task; if Orleans never called
        // Shutdown at all (silo died inside its budget), this is the only
        // path that disposes the librdkafka client.
        var drainTimeout = TimeSpan.FromSeconds(10);
        foreach (var receiver in receivers)
        {
            try
            {
                if (!receiver.Shutdown(drainTimeout).Wait(drainTimeout))
                {
                    _logger.LogWarning(
                        "Edict.Kafka receiver shutdown did not complete within {Timeout} during adapter dispose.",
                        drainTimeout);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Edict.Kafka receiver shutdown threw during adapter dispose.");
            }
        }
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
