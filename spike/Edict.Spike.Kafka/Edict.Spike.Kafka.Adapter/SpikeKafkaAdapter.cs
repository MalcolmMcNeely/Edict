using System.Text;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikeKafkaAdapter : IQueueAdapter
{
    readonly SpikeKafkaStreamOptions _options;
    readonly IProducer<string, byte[]> _producer;
    readonly SpikePartitionMapper _mapper;
    readonly SpikePreCriterionLog _log;
    readonly ILogger<SpikeKafkaAdapter> _logger;
    readonly ILoggerFactory _loggerFactory;

    public SpikeKafkaAdapter(
        string name,
        SpikeKafkaStreamOptions options,
        SpikePartitionMapper mapper,
        SpikePreCriterionLog log,
        ILoggerFactory loggerFactory)
    {
        Name = name;
        _options = options;
        _mapper = mapper;
        _log = log;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SpikeKafkaAdapter>();

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            CompressionType = CompressionType.Lz4,
            LingerMs = 5,
            MessageSendMaxRetries = int.MaxValue,
            ClientId = $"{name}-producer",
        };
        _producer = new ProducerBuilder<string, byte[]>(producerConfig).Build();
    }

    public string Name { get; }
    public bool IsRewindable => false;
    public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

    public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
    {
        var partition = SpikePartitionMapper.PartitionFor(queueId);
        return new SpikeKafkaReceiver(Name, partition, _options, _log, _loggerFactory.CreateLogger<SpikeKafkaReceiver>());
    }

    public async Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken token, Dictionary<string, object> requestContext)
    {
        var queueId = _mapper.GetQueueForStream(streamId);
        var partition = SpikePartitionMapper.PartitionFor(queueId);
        var keyString = streamId.GetKeyAsString() ?? streamId.ToString();
        var ns = streamId.GetNamespace() ?? string.Empty;

        var eventsArray = events.Cast<object>().ToArray();
        var payloads = new byte[eventsArray.Length][];
        for (var i = 0; i < eventsArray.Length; i++)
        {
            payloads[i] = SpikeKafkaWireFormat.SerializeEvent(eventsArray[i]);
        }

        var envelope = new SpikeKafkaWireEnvelope
        {
            StreamNamespace = ns,
            StreamKey = keyString,
            EventPayloads = payloads,
            RequestContext = requestContext,
        };

        var body = SpikeKafkaWireFormat.SerializeEnvelope(envelope);

        _log.Record(SpikeProbeKind.QueueMessageBatchEnter, keyString, partition: partition);

        var message = new Message<string, byte[]>
        {
            Key = keyString,
            Value = body,
            Headers = new Headers
            {
                { "edict.spike.ns", Encoding.UTF8.GetBytes(ns) },
            },
        };

        var topicPartition = new TopicPartition(_options.Topic, new Partition(partition));
        var report = await _producer.ProduceAsync(topicPartition, message);
        _logger.LogDebug("Spike produced offset {Offset} to {Topic}-{Partition} key={Key}", report.Offset.Value, report.Topic, report.Partition.Value, keyString);
    }
}
