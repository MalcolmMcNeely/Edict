using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Edict.Kafka;
using Edict.Kafka.Internal;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Edict.Kafka.Tests.Provisioning;

/// <summary>
/// Targeted unit tests for the topic provisioner's <c>EnsureTopicAsync</c>
/// seam. Runs against the assembly-shared Testcontainers Kafka (single
/// broker) so the auto-clamp and broker-count-mismatch branches exercise a
/// real cluster, not a mock. Each test mints its own topic name so parallel
/// fixture runs do not collide on the shared container.
/// </summary>
public sealed class EdictKafkaTopicProvisionerTests
{
    static async Task<IAdminClient> CreateAdminAsync()
    {
        var bootstrap = await KafkaAssemblyHost.GetBootstrapServersAsync();
        var config = new AdminClientConfig { BootstrapServers = bootstrap };
        return new AdminClientBuilder(config).Build();
    }

    static string UniqueTopicName() => $"edict-provisioner-test-{Guid.NewGuid():N}";

    [Fact]
    public async Task EnsureTopicAsync_ShouldCreateTopic_WithRequestedPartitionsAndReplicationFactor()
    {
        using var admin = await CreateAdminAsync();
        var topic = UniqueTopicName();

        await EdictKafkaTopicProvisioner.EnsureTopicAsync(
            admin,
            topic,
            partitionCount: 4,
            requestedReplicationFactor: 1,
            replicationFactorIsExplicit: false,
            NullLogger<EdictKafkaTopicProvisioner>.Instance,
            CancellationToken.None);

        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
        var topicMetadata = Assert.Single(metadata.Topics);
        Assert.Equal(topic, topicMetadata.Topic);
        Assert.Equal(4, topicMetadata.Partitions.Count);
        Assert.All(topicMetadata.Partitions, p => Assert.Single(p.Replicas));
    }

    [Fact]
    public async Task EnsureTopicAsync_ShouldThrow_WhenExplicitReplicationFactorExceedsBrokerCount()
    {
        using var admin = await CreateAdminAsync();
        var topic = UniqueTopicName();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EdictKafkaTopicProvisioner.EnsureTopicAsync(
                admin, topic, partitionCount: 1, requestedReplicationFactor: 3,
                replicationFactorIsExplicit: true,
                NullLogger<EdictKafkaTopicProvisioner>.Instance, CancellationToken.None));

        Assert.Contains(topic, ex.Message);
        Assert.Contains("replication factor 3", ex.Message);
        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
        // Refusal must be pre-create — no orphan topic left behind on the broker.
        Assert.Empty(metadata.Topics.Single().Partitions);
    }

    [Fact]
    public async Task EnsureTopicAsync_ShouldAutoClampReplicationFactor_WhenNotExplicitAndBrokerCountInsufficient()
    {
        using var admin = await CreateAdminAsync();
        var topic = UniqueTopicName();

        // Single-broker test cluster cannot satisfy rf=3 — with isExplicit=false
        // the provisioner clamps down to the available broker count rather than
        // throwing, so a dev cluster works without setup ceremony.
        await EdictKafkaTopicProvisioner.EnsureTopicAsync(
            admin, topic, partitionCount: 1, requestedReplicationFactor: 3,
            replicationFactorIsExplicit: false,
            NullLogger<EdictKafkaTopicProvisioner>.Instance, CancellationToken.None);

        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
        var topicMetadata = Assert.Single(metadata.Topics);
        var partition = Assert.Single(topicMetadata.Partitions);
        Assert.Single(partition.Replicas);
    }

    [Fact]
    public async Task EnsureTopicAsync_ShouldBeIdempotent_WhenCalledTwice()
    {
        using var admin = await CreateAdminAsync();
        var topic = UniqueTopicName();

        await EdictKafkaTopicProvisioner.EnsureTopicAsync(
            admin, topic, partitionCount: 2, requestedReplicationFactor: 1,
            replicationFactorIsExplicit: false,
            NullLogger<EdictKafkaTopicProvisioner>.Instance, CancellationToken.None);

        await EdictKafkaTopicProvisioner.EnsureTopicAsync(
            admin, topic, partitionCount: 2, requestedReplicationFactor: 1,
            replicationFactorIsExplicit: false,
            NullLogger<EdictKafkaTopicProvisioner>.Instance, CancellationToken.None);

        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));
        var topicMetadata = Assert.Single(metadata.Topics);
        Assert.Equal(2, topicMetadata.Partitions.Count);
    }

    [Fact]
    public async Task StartAsync_ShouldProvisionEveryRegisteredStream()
    {
        // Two stream names guarantees the per-stream loop is exercised — a
        // single-stream registry would still pass the slice-3 provisioner.
        var bootstrap = await KafkaAssemblyHost.GetBootstrapServersAsync();
        var topicA = UniqueTopicName();
        var topicB = UniqueTopicName();
        var options = new EdictKafkaStreamsOptions { BootstrapServers = bootstrap };
        var registry = new EdictKafkaStreamRegistry(new[] { topicA, topicB });
        var provisioner = new EdictKafkaTopicProvisioner(
            options, registry, NullLogger<EdictKafkaTopicProvisioner>.Instance);

        await provisioner.StartAsync(CancellationToken.None);

        using var admin = await CreateAdminAsync();
        var topicAMeta = admin.GetMetadata(topicA, TimeSpan.FromSeconds(10)).Topics.Single();
        var topicBMeta = admin.GetMetadata(topicB, TimeSpan.FromSeconds(10)).Topics.Single();
        Assert.NotEmpty(topicAMeta.Partitions);
        Assert.NotEmpty(topicBMeta.Partitions);
    }

    [Fact]
    public async Task StartAsync_ShouldHonourPerStreamPartitionOverride()
    {
        var bootstrap = await KafkaAssemblyHost.GetBootstrapServersAsync();
        var hotTopic = UniqueTopicName();
        var coldTopic = UniqueTopicName();
        var options = new EdictKafkaStreamsOptions
        {
            BootstrapServers = bootstrap,
            PartitionCount = 2,
        };
        options.PartitionCountByStream[hotTopic] = 6;
        var registry = new EdictKafkaStreamRegistry(new[] { hotTopic, coldTopic });
        var provisioner = new EdictKafkaTopicProvisioner(
            options, registry, NullLogger<EdictKafkaTopicProvisioner>.Instance);

        await provisioner.StartAsync(CancellationToken.None);

        using var admin = await CreateAdminAsync();
        var hot = admin.GetMetadata(hotTopic, TimeSpan.FromSeconds(10)).Topics.Single();
        var cold = admin.GetMetadata(coldTopic, TimeSpan.FromSeconds(10)).Topics.Single();
        Assert.Equal(6, hot.Partitions.Count);
        Assert.Equal(2, cold.Partitions.Count);
    }
}
