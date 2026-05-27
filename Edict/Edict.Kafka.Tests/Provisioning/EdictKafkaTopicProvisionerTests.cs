using Confluent.Kafka;
using Confluent.Kafka.Admin;

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
}
