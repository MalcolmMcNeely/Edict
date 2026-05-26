using Confluent.Kafka;

using Microsoft.Extensions.Logging;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikeKafkaReceiver : IQueueAdapterReceiver
{
    readonly string _providerName;
    readonly int _partition;
    readonly SpikeKafkaStreamOptions _options;
    readonly SpikePreCriterionLog _log;
    readonly ILogger _logger;

    IConsumer<string, byte[]>? _consumer;
    Task? _shutdownInFlight;

    public SpikeKafkaReceiver(
        string providerName,
        int partition,
        SpikeKafkaStreamOptions options,
        SpikePreCriterionLog log,
        ILogger logger)
    {
        _providerName = providerName;
        _partition = partition;
        _options = options;
        _log = log;
        _logger = logger;
    }

    public Task Initialize(TimeSpan timeout)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = _options.AutoOffsetReset,
            EnablePartitionEof = false,
            ClientId = $"{_providerName}-r-p{_partition}",
            AllowAutoCreateTopics = false,
        };
        _consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        _consumer.Assign(new TopicPartition(_options.Topic, new Partition(_partition)));
        _logger.LogInformation("Spike receiver initialised for partition {Partition} on topic {Topic}", _partition, _options.Topic);
        return Task.CompletedTask;
    }

    public Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
    {
        if (_consumer == null)
        {
            return Task.FromResult<IList<IBatchContainer>>(Array.Empty<IBatchContainer>());
        }

        _log.Record(SpikeProbeKind.GetQueueMessagesEnter, $"p{_partition}", partition: _partition);

        var batch = new List<IBatchContainer>();
        var cap = maxCount > 0 ? maxCount : 32;

        var deadline = DateTime.UtcNow + _options.PollTimeout;
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
                _logger.LogWarning(ex, "Spike receiver consume error on partition {Partition}", _partition);
                break;
            }
            if (result == null || result.Message == null)
            {
                break;
            }

            SpikeKafkaWireEnvelope envelope;
            object[] events;
            try
            {
                envelope = SpikeKafkaWireFormat.DeserializeEnvelope(result.Message.Value);
                events = new object[envelope.EventPayloads.Length];
                for (var i = 0; i < envelope.EventPayloads.Length; i++)
                {
                    events[i] = SpikeKafkaWireFormat.DeserializeEvent(envelope.EventPayloads[i]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Spike receiver deserialize failed at offset {Offset} partition {Partition}", result.Offset.Value, _partition);
                continue;
            }

            var streamId = StreamId.Create(envelope.StreamNamespace, envelope.StreamKey);
            var container = new SpikeKafkaBatchContainer(
                streamId,
                _partition,
                result.Offset.Value,
                events,
                envelope.RequestContext);
            batch.Add(container);
        }

        _log.Record(SpikeProbeKind.GetQueueMessagesExit, $"p{_partition}", partition: _partition);
        return Task.FromResult<IList<IBatchContainer>>(batch);
    }

    public Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
    {
        if (_consumer == null || messages.Count == 0)
        {
            return Task.CompletedTask;
        }

        long maxOffset = -1;
        foreach (var m in messages)
        {
            if (m is SpikeKafkaBatchContainer kc && kc.Partition == _partition && kc.Offset > maxOffset)
            {
                maxOffset = kc.Offset;
            }
            _log.Record(SpikeProbeKind.MessagesDeliveredAsync, m.StreamId.GetKeyAsString() ?? "", offset: (m as SpikeKafkaBatchContainer)?.Offset, partition: _partition);
        }
        if (maxOffset >= 0)
        {
            try
            {
                _consumer.Commit(new[] { new TopicPartitionOffset(_options.Topic, new Partition(_partition), new Offset(maxOffset + 1)) });
            }
            catch (KafkaException ex)
            {
                _logger.LogWarning(ex, "Spike receiver commit failed at offset {Offset}", maxOffset);
            }
        }
        return Task.CompletedTask;
    }

    public Task Shutdown(TimeSpan timeout)
    {
        if (_shutdownInFlight != null)
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
                _logger.LogWarning(ex, "Spike receiver shutdown error");
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
