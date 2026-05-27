using Confluent.Kafka;

namespace Edict.Kafka.Tests.AdapterContract;

/// <summary>
/// In-memory <see cref="IProducer{TKey, TValue}"/> for adapter-contract tests.
/// Records every <see cref="ProduceAsync"/> call so per-stream routing,
/// partition selection, and key/value framing are observable without a broker.
/// Members outside the adapter's call path throw
/// <see cref="NotImplementedException"/> so an unexpected adapter-side call is
/// loud, not silently green.
/// </summary>
sealed class FakeKafkaProducer : IProducer<string, byte[]>
{
    public List<(TopicPartition Partition, Message<string, byte[]> Message)> ProduceCalls { get; } = new();

    public bool WasDisposed { get; private set; }

    public bool WasFlushed { get; private set; }

    public Task<DeliveryResult<string, byte[]>> ProduceAsync(
        TopicPartition topicPartition,
        Message<string, byte[]> message,
        CancellationToken cancellationToken = default)
    {
        ProduceCalls.Add((topicPartition, message));
        var report = new DeliveryResult<string, byte[]>
        {
            Topic = topicPartition.Topic,
            Partition = topicPartition.Partition,
            Offset = new Offset(ProduceCalls.Count - 1),
            Message = message,
        };
        return Task.FromResult(report);
    }

    public int Flush(TimeSpan timeout)
    {
        WasFlushed = true;
        return 0;
    }

    public void Dispose() => WasDisposed = true;

    public Handle Handle => throw new NotImplementedException();

    public string Name => nameof(FakeKafkaProducer);

    public int AddBrokers(string brokers) => throw new NotImplementedException();

    public void SetSaslCredentials(string username, string password) => throw new NotImplementedException();

    public Task<DeliveryResult<string, byte[]>> ProduceAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public void Produce(
        string topic,
        Message<string, byte[]> message,
        Action<DeliveryReport<string, byte[]>>? deliveryHandler = null) => throw new NotImplementedException();

    public void Produce(
        TopicPartition topicPartition,
        Message<string, byte[]> message,
        Action<DeliveryReport<string, byte[]>>? deliveryHandler = null) => throw new NotImplementedException();

    public void Flush(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public int Poll(TimeSpan timeout) => throw new NotImplementedException();

    public void InitTransactions(TimeSpan timeout) => throw new NotImplementedException();

    public void BeginTransaction() => throw new NotImplementedException();

    public void CommitTransaction(TimeSpan timeout) => throw new NotImplementedException();

    public void CommitTransaction() => throw new NotImplementedException();

    public void AbortTransaction(TimeSpan timeout) => throw new NotImplementedException();

    public void AbortTransaction() => throw new NotImplementedException();

    public void SendOffsetsToTransaction(
        IEnumerable<TopicPartitionOffset> offsets,
        IConsumerGroupMetadata groupMetadata,
        TimeSpan timeout) => throw new NotImplementedException();
}
