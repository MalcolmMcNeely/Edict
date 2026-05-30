using Confluent.Kafka;

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
    readonly object _shutdownLock = new();

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
        // Bound the per-call work so the Orleans pulling agent's task is not
        // parked on an empty partition for longer than necessary. Consume()
        // returns as soon as a record is available (it does not always block
        // the full timeout), so under load the loop fills the batch quickly
        // and breaks on the first null. The 20 ms ceiling matters only when
        // the partition is idle — idle delivery-latency floor.
        var pollTimeout = TimeSpan.FromMilliseconds(20);
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
            catch (ConsumeException exception)
            {
                _logger.LogWarning(exception, "Edict.Kafka receiver consume error on partition {Partition}", _partition);
                break;
            }
            if (result is null || result.Message is null)
            {
                break;
            }

            KafkaWireEnvelope envelope;
            try
            {
                envelope = _serializer.Deserialize<KafkaWireEnvelope>(result.Message.Value);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
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
            catch (KafkaException exception)
            {
                _logger.LogWarning(exception, "Edict.Kafka commit failed at offset {Offset}", maxOffset);
            }
        }

        return Task.CompletedTask;
    }

    public Task Shutdown(TimeSpan timeout)
    {
        // Idempotent — Orleans and EdictKafkaAdapter.Dispose can both call
        // Shutdown concurrently, and the inner Task.Run must run exactly once
        // (consumer.Close() is not re-entrant — duplicate Close+Dispose
        // throws ObjectDisposedException). Both callers await the same Task.
        lock (_shutdownLock)
        {
            _shutdownInFlight ??= Task.Run(() =>
            {
                try
                {
                    _consumer?.Close();
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Edict.Kafka receiver shutdown error");
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
}
