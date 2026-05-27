using Confluent.Kafka;

using Edict.Contracts.Configuration;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Kafka.Internal;
using Edict.Kafka.Wire;
using Edict.Tests.Conformance;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Runtime;
using Orleans.Serialization;

using Xunit;

namespace Edict.Kafka.Tests.AdapterContract;

/// <summary>
/// Adapter-contract layer for <see cref="EdictKafkaAdapter"/>: drives the
/// producer side against a <see cref="FakeKafkaProducer"/> so per-stream topic
/// routing is observable without a broker. ADR-0028 §2 records the
/// per-<see cref="Contracts.Events.EdictStreamAttribute"/> topology — every
/// domain stream maps to its own Kafka topic — and this layer is the
/// deterministic guard against a regression to the slice-1 single-topic shape.
/// </summary>
public sealed class EdictKafkaAdapterContractTests
{
    const string TestProvider = "edict-kafka";
    const string TestBootstrap = "localhost:9092";
    const string TestGroup = "edict-kafka-tests-fake";

    static readonly EdictKafkaStreamsOptions Options = new()
    {
        BootstrapServers = TestBootstrap,
        ConsumerGroupId = TestGroup,
        PartitionCount = 4,
    };

    static readonly Serializer Serializer = BuildSerializer();

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(s => s
            .AddAssembly(typeof(EdictKafkaWireEnvelope).Assembly)
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    static EdictKafkaAdapter CreateAdapter(
        FakeKafkaProducer producer,
        EdictKafkaStreamsOptions? options = null,
        params string[] knownStreams)
    {
        var opt = options ?? Options;
        var registry = new EdictKafkaStreamRegistry(
            knownStreams.Length == 0 ? new[] { "OrdersStream", "InventoryStream" } : knownStreams);
        var mapper = new EdictKafkaPartitionMapper(opt, registry);
        var loggerFactory = NullLoggerFactory.Instance;
        return new EdictKafkaAdapter(
            TestProvider,
            opt,
            mapper,
            Serializer,
            loggerFactory,
            _ => producer);
    }

    [Fact]
    public async Task QueueMessageBatchAsync_ShouldProduceToTopicNamedAfterStreamNamespace()
    {
        var producer = new FakeKafkaProducer();
        var adapter = CreateAdapter(producer);
        var streamId = StreamId.Create("OrdersStream", "order-7");

        await adapter.QueueMessageBatchAsync(streamId, new object[] { "evt-a" }, token: null!, requestContext: new());

        var call = Assert.Single(producer.ProduceCalls);
        Assert.Equal("OrdersStream", call.Partition.Topic);
        Assert.Equal("order-7", call.Message.Key);
    }

    [Fact]
    public async Task QueueMessageBatchAsync_ShouldRouteDifferentStreamsToDifferentTopics()
    {
        var producer = new FakeKafkaProducer();
        var adapter = CreateAdapter(producer);

        await adapter.QueueMessageBatchAsync(
            StreamId.Create("OrdersStream", "k-a"), new object[] { "evt-1" }, null!, new());
        await adapter.QueueMessageBatchAsync(
            StreamId.Create("InventoryStream", "k-b"), new object[] { "evt-2" }, null!, new());

        Assert.Equal(2, producer.ProduceCalls.Count);
        Assert.Equal("OrdersStream", producer.ProduceCalls[0].Partition.Topic);
        Assert.Equal("InventoryStream", producer.ProduceCalls[1].Partition.Topic);
    }

    [Fact]
    public async Task CreateReceiver_ShouldBindToTopicAndPartitionDecodedFromQueueId()
    {
        var producer = new FakeKafkaProducer();
        var consumer = new FakeKafkaConsumer();
        var registry = new EdictKafkaStreamRegistry(new[] { "OrdersStream" });
        var mapper = new EdictKafkaPartitionMapper(Options, registry);
        var adapter = new EdictKafkaAdapter(
            TestProvider, Options, mapper, Serializer, NullLoggerFactory.Instance,
            _ => producer, (_, _, _) => consumer);

        var queueId = mapper.GetQueueForStream(StreamId.Create("OrdersStream", "order-1"));
        var expectedPartition = EdictKafkaPartitionMapper.PartitionFor(queueId);

        var receiver = adapter.CreateReceiver(queueId);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        var assign = Assert.Single(consumer.AssignCalls);
        Assert.Equal("OrdersStream", assign.Topic);
        Assert.Equal(expectedPartition, assign.Partition.Value);
    }

    [Fact]
    public void Dispose_ShouldFlushAndDisposeTheProducer()
    {
        var producer = new FakeKafkaProducer();
        var adapter = CreateAdapter(producer);

        adapter.Dispose();

        Assert.True(producer.WasFlushed);
        Assert.True(producer.WasDisposed);
    }
}
