using Confluent.Kafka;

using Edict.Kafka.Wire;

using Microsoft.Extensions.Logging;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// One <see cref="IQueueAdapterReceiver"/> per Kafka partition. Commits the
/// broker offset only inside <see cref="MessagesDeliveredAsync"/>, which
/// Orleans calls after the consumer grain's <c>HandleAsync</c> returns —
/// validated by the pre-criterion in spike 0001. That ordering is the lever
/// the ADR-0002 contract rides on; do not move the commit earlier.
/// </summary>
sealed class EdictKafkaReceiver : IQueueAdapterReceiver
{
    readonly string _providerName;
    readonly int _partition;
    readonly string _topic;
    readonly EdictKafkaStreamsOptions _options;
    readonly Serializer _serializer;
    readonly ILogger _logger;
    readonly Func<IConsumer<string, byte[]>> _consumerFactory;

    IConsumer<string, byte[]>? _consumer;
    Task? _shutdownInFlight;

    public EdictKafkaReceiver(
        string providerName,
        int partition,
        string topic,
        EdictKafkaStreamsOptions options,
        Serializer serializer,
        ILogger logger,
        Func<IConsumer<string, byte[]>>? consumerFactory = null)
    {
        _providerName = providerName;
        _partition = partition;
        _topic = topic;
        _options = options;
        _serializer = serializer;
        _logger = logger;
        _consumerFactory = consumerFactory ?? BuildDefaultConsumer;
    }

    public Task Initialize(TimeSpan timeout)
    {
        _consumer = _consumerFactory();
        _consumer.Assign(new TopicPartition(_topic, new Partition(_partition)));
        _logger.LogInformation(
            "Edict.Kafka receiver initialised for partition {Partition} on topic {Topic} group {Group}",
            _partition, _topic, _options.ConsumerGroupId);
        return Task.CompletedTask;
    }

    IConsumer<string, byte[]> BuildDefaultConsumer()
    {
        var config = EdictKafkaConsumerConfigFactory.Build(_options, clientId: $"{_providerName}-r-p{_partition}");
        return new ConsumerBuilder<string, byte[]>(config).Build();
    }

    public Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
    {
        if (_consumer is null)
        {
            return Task.FromResult<IList<IBatchContainer>>(Array.Empty<IBatchContainer>());
        }

        var batch = new List<IBatchContainer>();
        var cap = maxCount > 0 ? maxCount : 32;
        var pollTimeout = TimeSpan.FromMilliseconds(200);
        var deadline = DateTime.UtcNow + pollTimeout;

        while (batch.Count < cap && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            ConsumeResult<string, byte[]>? result;
            try
            {
                result = _consumer.Consume(remaining);
            }
            catch (ConsumeException ex)
            {
                _logger.LogWarning(ex, "Edict.Kafka receiver consume error on partition {Partition}", _partition);
                break;
            }
            if (result is null || result.Message is null)
            {
                break;
            }

            EdictKafkaWireEnvelope envelope;
            try
            {
                envelope = _serializer.Deserialize<EdictKafkaWireEnvelope>(result.Message.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Edict.Kafka deserialize failed at offset {Offset} partition {Partition}; skipping",
                    result.Offset.Value, _partition);
                continue;
            }

            var streamId = StreamId.Create(envelope.StreamNamespace, envelope.StreamKey);
            batch.Add(new EdictKafkaBatchContainer(streamId, _partition, result.Offset.Value, envelope.Events));
        }

        return Task.FromResult<IList<IBatchContainer>>(batch);
    }

    public Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
    {
        if (_consumer is null || messages.Count == 0)
        {
            return Task.CompletedTask;
        }

        long maxOffset = -1;
        foreach (var m in messages)
        {
            if (m is EdictKafkaBatchContainer kc && kc.Partition == _partition && kc.Offset > maxOffset)
            {
                maxOffset = kc.Offset;
            }
        }

        if (maxOffset >= 0)
        {
            try
            {
                _consumer.Commit(new[]
                {
                    new TopicPartitionOffset(_topic, new Partition(_partition), new Offset(maxOffset + 1)),
                });
            }
            catch (KafkaException ex)
            {
                _logger.LogWarning(ex, "Edict.Kafka commit failed at offset {Offset}", maxOffset);
            }
        }

        return Task.CompletedTask;
    }

    public Task Shutdown(TimeSpan timeout)
    {
        if (_shutdownInFlight is not null)
        {
            return _shutdownInFlight;
        }

        _shutdownInFlight = Task.Run(() =>
        {
            try
            {
                _consumer?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Edict.Kafka receiver shutdown error");
            }
            finally
            {
                _consumer?.Dispose();
                _consumer = null;
            }
        });
        return _shutdownInFlight;
    }
}
