using Edict.Kafka;
using Edict.Kafka.Internal;

using Orleans.Runtime;
using Orleans.Streams;

using Xunit;

namespace Edict.Kafka.Tests.Mapper;

/// <summary>
/// Targeted unit tests for <see cref="EdictKafkaPartitionMapper"/> under the
/// per-stream topology: every <c>[EdictStream]</c> name in the registry gets
/// its own queue fan-out, the per-stream partition override is honoured, and
/// the queue identity carries the topic + partition through Orleans' opaque
/// <see cref="QueueId"/> so the receiver factory can decode where to
/// subscribe.
/// </summary>
public sealed class EdictKafkaPartitionMapperTests
{
    static EdictKafkaStreamRegistry RegistryOf(params string[] names) => new(names);

    [Fact]
    public void GetAllQueues_ShouldEnumerateOneQueuePerStreamPartition()
    {
        var registry = RegistryOf("Alpha", "Beta");
        var options = new EdictKafkaStreamsOptions { PartitionCount = 4 };

        var mapper = new EdictKafkaPartitionMapper(options, registry);

        Assert.Equal(8, mapper.GetAllQueues().Count());
        Assert.Equal(
            new[] { 4, 4 },
            new[] { "Alpha", "Beta" }
                .Select(s => mapper.GetAllQueues().Count(q => EdictKafkaPartitionMapper.TopicFor(q) == s))
                .ToArray());
    }

    [Fact]
    public void GetAllQueues_ShouldHonourPerStreamPartitionOverride()
    {
        var registry = RegistryOf("Alpha", "Beta");
        var options = new EdictKafkaStreamsOptions { PartitionCount = 4 };
        options.PartitionCountByStream["Beta"] = 2;

        var mapper = new EdictKafkaPartitionMapper(options, registry);

        Assert.Equal(4, mapper.GetAllQueues().Count(q => EdictKafkaPartitionMapper.TopicFor(q) == "Alpha"));
        Assert.Equal(2, mapper.GetAllQueues().Count(q => EdictKafkaPartitionMapper.TopicFor(q) == "Beta"));
    }

    [Fact]
    public void GetQueueForStream_ShouldRouteToATopicQueueDeterministically()
    {
        var registry = RegistryOf("Alpha");
        var options = new EdictKafkaStreamsOptions { PartitionCount = 4 };
        var mapper = new EdictKafkaPartitionMapper(options, registry);

        var first = mapper.GetQueueForStream(StreamId.Create("Alpha", "key-7"));
        var second = mapper.GetQueueForStream(StreamId.Create("Alpha", "key-7"));

        Assert.Equal(first, second);
        Assert.Equal("Alpha", EdictKafkaPartitionMapper.TopicFor(first));
    }

    [Fact]
    public void GetQueueForStream_ShouldSelectFromTheStreamsOwnPartitions()
    {
        var registry = RegistryOf("Alpha", "Beta");
        var options = new EdictKafkaStreamsOptions { PartitionCount = 4 };
        var mapper = new EdictKafkaPartitionMapper(options, registry);

        var alpha = mapper.GetQueueForStream(StreamId.Create("Alpha", "k"));
        var beta = mapper.GetQueueForStream(StreamId.Create("Beta", "k"));

        Assert.Equal("Alpha", EdictKafkaPartitionMapper.TopicFor(alpha));
        Assert.Equal("Beta", EdictKafkaPartitionMapper.TopicFor(beta));
    }

    [Fact]
    public void GetQueueForStream_ShouldThrow_WhenStreamIsNotRegistered()
    {
        var registry = RegistryOf("Alpha");
        var options = new EdictKafkaStreamsOptions { PartitionCount = 4 };
        var mapper = new EdictKafkaPartitionMapper(options, registry);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            mapper.GetQueueForStream(StreamId.Create("Ghost", "k")));

        Assert.Contains("Ghost", exception.Message);
    }

    [Fact]
    public void TopicAndPartitionFor_ShouldRoundTripThroughQueueId()
    {
        var registry = RegistryOf("Alpha");
        var options = new EdictKafkaStreamsOptions { PartitionCount = 4 };
        var mapper = new EdictKafkaPartitionMapper(options, registry);

        foreach (var queue in mapper.GetAllQueues())
        {
            var topic = EdictKafkaPartitionMapper.TopicFor(queue);
            var partition = EdictKafkaPartitionMapper.PartitionFor(queue);

            Assert.Equal("Alpha", topic);
            Assert.InRange(partition, 0, 3);
        }
    }
}
