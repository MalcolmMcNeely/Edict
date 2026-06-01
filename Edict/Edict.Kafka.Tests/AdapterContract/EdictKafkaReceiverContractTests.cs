using Confluent.Kafka;

using Edict.Contracts.Configuration;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Kafka.Internal;
using Edict.Tests.Conformance;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;

using Xunit;

using ErrorCode = Confluent.Kafka.ErrorCode;

namespace Edict.Kafka.Tests.AdapterContract;

/// <summary>
/// Drives <see cref="EdictKafkaReceiver"/> against a fake
/// <see cref="IConsumer{TKey, TValue}"/> so the commit-watermark invariants
/// are observable without standing up Kafka. These are deterministic
/// properties of the receiver — no timing windows, no consumer-group
/// rebalance simulation.
/// </summary>
public sealed class EdictKafkaReceiverContractTests
{
    const string TestTopic = "edict-events";
    const int TestPartition = 3;
    const string TestProvider = "edict-kafka";
    const string TestBootstrap = "localhost:9092";
    const string TestGroup = "edict-kafka-tests-fake";

    static readonly EdictKafkaStreamsOptions Options = new()
    {
        BootstrapServers = TestBootstrap,
        ConsumerGroupId = TestGroup,
    };

    static readonly Serializer Serializer = BuildSerializer();

    static Serializer BuildSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(s => s
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());
        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    static EdictKafkaReceiver CreateReceiver(FakeKafkaConsumer consumer, int partition = TestPartition) =>
        new(
            TestProvider,
            partition,
            TestTopic,
            Options,
            Serializer,
            NullLogger<EdictKafkaReceiver>.Instance,
            () => consumer);

    [Fact]
    public async Task Initialize_ShouldAssignTheReceiverPartition()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);

        await receiver.Initialize(TimeSpan.FromSeconds(5));

        var assign = Assert.Single(consumer.AssignCalls);
        Assert.Equal(TestTopic, assign.Topic);
        Assert.Equal(TestPartition, assign.Partition.Value);
    }

    [Fact]
    public async Task GetQueueMessagesAsync_ShouldYieldBatchContainerForEachConsumedRecord()
    {
        var consumer = new FakeKafkaConsumer();
        consumer.EnqueueRecord(BuildRecord(streamNamespace: "orders", streamKey: "order-7", offset: 42, events: ["edictEvent-a", 99]));
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        var batch = await receiver.GetQueueMessagesAsync(maxCount: 32);

        var container = (EdictKafkaBatchContainer)Assert.Single(batch);
        Assert.Equal(TestPartition, container.Partition);
        Assert.Equal(42L, container.Offset);
        Assert.Equal("orders", container.StreamId.GetNamespace());
        Assert.Equal("order-7", container.StreamId.GetKeyAsString());
        var events = container.GetEvents<object>().Select(t => t.Item1).ToArray();
        Assert.Equal(new object[] { "edictEvent-a", 99 }, events);
    }

    [Fact]
    public async Task MessagesDeliveredAsync_ShouldCommitMaxOffsetPlusOne()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        var batch = new IBatchContainer[]
        {
            BuildContainer(offset: 10),
            BuildContainer(offset: 11),
            BuildContainer(offset: 12),
        };
        await receiver.MessagesDeliveredAsync(batch);

        var commit = Assert.Single(consumer.CommittedOffsets);
        Assert.Equal(TestTopic, commit.Topic);
        Assert.Equal(TestPartition, commit.Partition.Value);
        Assert.Equal(13L, commit.Offset.Value);
    }

    [Fact]
    public async Task MessagesDeliveredAsync_ShouldCommitOnlyThePartialMax_WhenSomeOffsetsAreUnacked()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        // Receiver fetched offsets 10/11/12 but only the first two are acked.
        var acked = new IBatchContainer[]
        {
            BuildContainer(offset: 10),
            BuildContainer(offset: 11),
        };
        await receiver.MessagesDeliveredAsync(acked);

        var commit = Assert.Single(consumer.CommittedOffsets);
        Assert.Equal(12L, commit.Offset.Value);
    }

    [Fact]
    public async Task MessagesDeliveredAsync_ShouldNotCommit_WhenBatchIsEmpty()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        await receiver.MessagesDeliveredAsync(Array.Empty<IBatchContainer>());

        Assert.Empty(consumer.CommittedOffsets);
    }

    [Fact]
    public async Task NeverCallingMessagesDeliveredAsync_ShouldLeaveOffsetUncommitted()
    {
        // The deterministic shape of "rebalance redelivers uncommitted messages":
        // if MessagesDeliveredAsync is never invoked, no Commit() is ever called
        // on the consumer, so a rebalanced replacement consumer will resume from
        // the last broker-side committed offset and re-yield the same record.
        var consumer = new FakeKafkaConsumer();
        consumer.EnqueueRecord(BuildRecord("ns", "k", offset: 7, events: ["x"]));
        var receiver = CreateReceiver(consumer);

        await receiver.Initialize(TimeSpan.FromSeconds(5));
        _ = await receiver.GetQueueMessagesAsync(maxCount: 32);

        Assert.Empty(consumer.CommittedOffsets);
    }

    [Fact]
    public async Task MessagesDeliveredAsync_ShouldIgnoreContainersFromForeignPartitions()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer, partition: 1);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        // Only containers stamped with this receiver's partition feed the commit
        // watermark. The foreign-partition one (partition 5) must be ignored —
        // otherwise a rebalance-shared queue cache could advance a partition's
        // commit past records the receiver never owned.
        var batch = new IBatchContainer[]
        {
            BuildContainer(offset: 100, partition: 5),
        };
        await receiver.MessagesDeliveredAsync(batch);

        Assert.Empty(consumer.CommittedOffsets);
    }

    [Fact]
    public async Task Shutdown_ShouldCloseAndDisposeTheConsumer()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        await receiver.Shutdown(TimeSpan.FromSeconds(5));

        Assert.True(consumer.WasClosed);
        Assert.True(consumer.WasDisposed);
    }

    [Fact]
    public async Task GetQueueMessagesAsync_ShouldReturnEmpty_WhenCalledBeforeInitialize()
    {
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);

        var batch = await receiver.GetQueueMessagesAsync(maxCount: 32);

        Assert.Empty(batch);
    }

    [Fact]
    public async Task MessagesDeliveredAsync_ShouldNotThrow_WhenCalledBeforeInitialize()
    {
        var receiver = CreateReceiver(new FakeKafkaConsumer());

        await receiver.MessagesDeliveredAsync(new IBatchContainer[] { BuildContainer(offset: 1) });
    }

    [Fact]
    public async Task GetQueueMessagesAsync_ShouldSkipUndeserialisableRecord_AndStillYieldTheNext()
    {
        // A poison record (bytes that are not a KafkaWireEnvelope) must not
        // stall the partition: the receiver logs and continues, so the next
        // well-formed record is still delivered.
        var consumer = new FakeKafkaConsumer();
        consumer.EnqueueRecord(BuildPoisonRecord(offset: 41));
        consumer.EnqueueRecord(BuildRecord("orders", "order-8", offset: 42, events: ["ok"]));
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        var batch = await receiver.GetQueueMessagesAsync(maxCount: 32);

        var container = (EdictKafkaBatchContainer)Assert.Single(batch);
        Assert.Equal(42L, container.Offset);
        Assert.Equal("order-8", container.StreamId.GetKeyAsString());
    }

    [Fact]
    public async Task GetQueueMessagesAsync_ShouldStopBatching_WhenConsumeThrows()
    {
        var consumer = new FakeKafkaConsumer
        {
            ConsumeError = new ConsumeException(new ConsumeResult<byte[], byte[]>(), new Error(ErrorCode.Local_Fail)),
        };
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        var batch = await receiver.GetQueueMessagesAsync(maxCount: 32);

        Assert.Empty(batch);
    }

    [Fact]
    public async Task MessagesDeliveredAsync_ShouldSwallowCommitFailure_SoTheBrokerRedelivers()
    {
        // A failed commit must not propagate: Orleans would otherwise see the
        // delivery callback fault. Leaving the offset uncommitted lets the
        // broker redeliver, which the idempotency ring then dedups.
        var consumer = new FakeKafkaConsumer { CommitError = new KafkaException(ErrorCode.Local_Fail) };
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        await receiver.MessagesDeliveredAsync(new IBatchContainer[] { BuildContainer(offset: 5) });

        Assert.Empty(consumer.CommittedOffsets);
    }

    [Fact]
    public async Task Shutdown_CalledConcurrently_ClosesTheConsumerExactlyOnce()
    {
        // consumer.Close() is not re-entrant; Orleans and the adapter can both
        // call Shutdown, so the close must run once no matter how many callers.
        var consumer = new FakeKafkaConsumer();
        var receiver = CreateReceiver(consumer);
        await receiver.Initialize(TimeSpan.FromSeconds(5));

        await Task.WhenAll(
            receiver.Shutdown(TimeSpan.FromSeconds(5)),
            receiver.Shutdown(TimeSpan.FromSeconds(5)));

        Assert.Equal(1, consumer.CloseCallCount);
        Assert.True(consumer.WasDisposed);
    }

    static EdictKafkaBatchContainer BuildContainer(long offset, int partition = TestPartition) =>
        new(StreamId.Create("ns", $"stream-{offset}"), partition, offset, events: []);

    static ConsumeResult<string, byte[]> BuildPoisonRecord(long offset) =>
        new()
        {
            Topic = TestTopic,
            Partition = new Partition(TestPartition),
            Offset = new Offset(offset),
            Message = new Message<string, byte[]> { Key = "poison", Value = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC } },
        };

    static ConsumeResult<string, byte[]> BuildRecord(string streamNamespace, string streamKey, long offset, object[] events)
    {
        var envelope = new KafkaWireEnvelope
        {
            StreamNamespace = streamNamespace,
            StreamKey = streamKey,
            Events = events,
        };
        var body = Serializer.SerializeToArray(envelope);
        return new ConsumeResult<string, byte[]>
        {
            Topic = TestTopic,
            Partition = new Partition(TestPartition),
            Offset = new Offset(offset),
            Message = new Message<string, byte[]> { Key = streamKey, Value = body },
        };
    }
}
