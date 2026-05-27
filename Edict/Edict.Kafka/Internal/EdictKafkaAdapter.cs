using Confluent.Kafka;

using Edict.Kafka.Wire;

using Microsoft.Extensions.Logging;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// Custom Orleans <see cref="IQueueAdapter"/> over <c>Confluent.Kafka</c>.
/// One producer instance per silo; one topic for now (slice-1 tracer bullet);
/// partition selected by the partition mapper so the same aggregate routes to
/// the same partition every time. Producer floors are hardcoded: <c>acks=all</c>,
/// idempotent producer on, lz4 compression — these are the contract floors
/// ADR-0028 records, not consumer-tunable.
/// </summary>
sealed class EdictKafkaAdapter : IQueueAdapter, IDisposable
{
    internal const string TopicName = "edict-events";

    readonly EdictKafkaStreamsOptions _options;
    readonly EdictKafkaPartitionMapper _mapper;
    readonly Serializer _serializer;
    readonly IProducer<string, byte[]> _producer;
    readonly ILogger<EdictKafkaAdapter> _logger;
    readonly ILoggerFactory _loggerFactory;

    public EdictKafkaAdapter(
        string name,
        EdictKafkaStreamsOptions options,
        EdictKafkaPartitionMapper mapper,
        Serializer serializer,
        ILoggerFactory loggerFactory)
    {
        Name = name;
        _options = options;
        _mapper = mapper;
        _serializer = serializer;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<EdictKafkaAdapter>();

        var producerConfig = EdictKafkaProducerConfigFactory.Build(options, clientId: $"{name}-producer");
        _producer = new ProducerBuilder<string, byte[]>(producerConfig).Build();
    }

    public string Name { get; }

    public bool IsRewindable => false;

    public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

    public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
    {
        var partition = EdictKafkaPartitionMapper.PartitionFor(queueId);
        return new EdictKafkaReceiver(
            Name,
            partition,
            TopicName,
            _options,
            _serializer,
            _loggerFactory.CreateLogger<EdictKafkaReceiver>());
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
        var topicPartition = new TopicPartition(TopicName, new Partition(partition));
        var report = await _producer.ProduceAsync(topicPartition, message);
        _logger.LogDebug(
            "Edict.Kafka produced offset {Offset} to {Topic}-{Partition} key={Key} (n={Count})",
            report.Offset.Value, report.Topic, report.Partition.Value, keyString, envelope.Events.Length);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
