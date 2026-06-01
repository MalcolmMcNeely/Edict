using Confluent.Kafka;

namespace Edict.Kafka.Tests.AdapterContract;

/// <summary>
/// In-memory <see cref="IConsumer{TKey, TValue}"/> for adapter-contract tests.
/// Exposes recorded assigns, commits, and the close/dispose flags; queue
/// <see cref="ConsumeResult{TKey, TValue}"/> values via <see cref="EnqueueRecord"/>
/// to drive <see cref="EdictKafkaReceiver.GetQueueMessagesAsync"/>. Members
/// outside the receiver's call path throw <see cref="NotImplementedException"/>
/// so an unexpected receiver-side call is loud, not silently green.
/// </summary>
sealed class FakeKafkaConsumer : IConsumer<string, byte[]>
{
    readonly Queue<ConsumeResult<string, byte[]>> _records = new();

    public List<TopicPartition> AssignCalls { get; } = new();

    public List<TopicPartitionOffset> CommittedOffsets { get; } = new();

    public int CloseCallCount { get; private set; }

    public bool WasClosed => CloseCallCount > 0;

    public bool WasDisposed { get; private set; }

    // When set, the matching call throws instead of recording — drives the
    // receiver's consume-error and commit-error handling paths.
    public ConsumeException? ConsumeError { get; set; }

    public KafkaException? CommitError { get; set; }

    public void EnqueueRecord(ConsumeResult<string, byte[]> record) => _records.Enqueue(record);

    public void Assign(TopicPartition partition) => AssignCalls.Add(partition);

    public ConsumeResult<string, byte[]>? Consume(TimeSpan timeout)
    {
        if (ConsumeError is not null)
        {
            throw ConsumeError;
        }
        return _records.Count == 0 ? null : _records.Dequeue();
    }

    public void Commit(IEnumerable<TopicPartitionOffset> offsets)
    {
        if (CommitError is not null)
        {
            throw CommitError;
        }
        CommittedOffsets.AddRange(offsets);
    }

    public void Close() => CloseCallCount++;

    public void Dispose() => WasDisposed = true;

    public Handle Handle => throw new NotImplementedException();

    public string Name => nameof(FakeKafkaConsumer);

    public string MemberId => throw new NotImplementedException();

    public List<TopicPartition> Assignment => AssignCalls;

    public List<string> Subscription => throw new NotImplementedException();

    public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotImplementedException();

    public int AddBrokers(string brokers) => throw new NotImplementedException();

    public void SetSaslCredentials(string username, string password) => throw new NotImplementedException();

    public void Subscribe(IEnumerable<string> topics) => throw new NotImplementedException();

    public void Subscribe(string topic) => throw new NotImplementedException();

    public void Unsubscribe() => throw new NotImplementedException();

    public void Assign(TopicPartitionOffset partition) => throw new NotImplementedException();

    public void Assign(IEnumerable<TopicPartitionOffset> partitions) => throw new NotImplementedException();

    public void Assign(IEnumerable<TopicPartition> partitions) => throw new NotImplementedException();

    public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) => throw new NotImplementedException();

    public void IncrementalAssign(IEnumerable<TopicPartition> partitions) => throw new NotImplementedException();

    public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) => throw new NotImplementedException();

    public void Unassign() => throw new NotImplementedException();

    public void StoreOffset(ConsumeResult<string, byte[]> result) => throw new NotImplementedException();

    public void StoreOffset(TopicPartitionOffset offset) => throw new NotImplementedException();

    public List<TopicPartitionOffset> Commit() => throw new NotImplementedException();

    public void Commit(ConsumeResult<string, byte[]> result) => throw new NotImplementedException();

    public void Seek(TopicPartitionOffset tpo) => throw new NotImplementedException();

    public void Pause(IEnumerable<TopicPartition> partitions) => throw new NotImplementedException();

    public void Resume(IEnumerable<TopicPartition> partitions) => throw new NotImplementedException();

    public List<TopicPartitionOffset> Committed(TimeSpan timeout) => throw new NotImplementedException();

    public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) =>
        throw new NotImplementedException();

    public Offset Position(TopicPartition partition) => throw new NotImplementedException();

    public List<TopicPartitionOffset> OffsetsForTimes(
        IEnumerable<TopicPartitionTimestamp> timestampsToSearch,
        TimeSpan timeout) => throw new NotImplementedException();

    public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => throw new NotImplementedException();

    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) =>
        throw new NotImplementedException();

    public ConsumeResult<string, byte[]> Consume(int millisecondsTimeout) => throw new NotImplementedException();

    public ConsumeResult<string, byte[]> Consume(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
