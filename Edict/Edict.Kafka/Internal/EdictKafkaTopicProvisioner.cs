using Confluent.Kafka;
using Confluent.Kafka.Admin;

using KafkaErrorCode = Confluent.Kafka.ErrorCode;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edict.Kafka.Internal;

/// <summary>
/// Creates the Edict-owned Kafka topic at silo startup with the configured
/// partition count. Idempotent — re-running against an existing topic is a
/// no-op. Mirrors the Postgres DDL bootstrap discipline: framework owns
/// substrate provisioning so a fresh broker works without setup scripts.
/// </summary>
sealed class EdictKafkaTopicProvisioner : IHostedService
{
    readonly EdictKafkaStreamsOptions _options;
    readonly EdictKafkaStreamRegistry _streamRegistry;
    readonly ILogger<EdictKafkaTopicProvisioner> _logger;

    public EdictKafkaTopicProvisioner(
        EdictKafkaStreamsOptions options,
        EdictKafkaStreamRegistry streamRegistry,
        ILogger<EdictKafkaTopicProvisioner> logger)
    {
        _options = options;
        _streamRegistry = streamRegistry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_streamRegistry.StreamNames.Count == 0)
        {
            _logger.LogWarning(
                "Edict.Kafka topic provisioner found no [EdictStream] events in the loaded assemblies. " +
                "No topics will be provisioned — handlers will throw on first publish until events are referenced.");
            return;
        }

        var adminConfig = new AdminClientConfig { BootstrapServers = _options.BootstrapServers };
        using var admin = new AdminClientBuilder(adminConfig).Build();
        foreach (var streamName in _streamRegistry.StreamNames)
        {
            await EnsureTopicAsync(
                admin,
                streamName,
                _options.PartitionCountFor(streamName),
                _options.ReplicationFactor,
                _options.IsReplicationFactorExplicit,
                _logger,
                cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Ensures the topic exists with the requested partition count and a
    /// replication factor satisfying the rf rule: when
    /// <paramref name="replicationFactorIsExplicit"/> is false the rf is
    /// clamped down to the broker count if the cluster cannot satisfy the
    /// request, when true the call throws on a mismatch instead. Idempotent —
    /// re-running against an existing topic is a no-op (no metadata change is
    /// applied to an already-created topic).
    /// </summary>
    internal static async Task EnsureTopicAsync(
        IAdminClient admin,
        string topicName,
        int partitionCount,
        short requestedReplicationFactor,
        bool replicationFactorIsExplicit,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var brokerCount = admin.GetMetadata(TimeSpan.FromSeconds(10)).Brokers.Count;
        var effectiveReplicationFactor = ResolveReplicationFactor(
            requestedReplicationFactor,
            replicationFactorIsExplicit,
            brokerCount,
            topicName);

        var spec = new TopicSpecification
        {
            Name = topicName,
            NumPartitions = partitionCount,
            ReplicationFactor = effectiveReplicationFactor,
        };

        try
        {
            await admin.CreateTopicsAsync(
                [spec],
                new CreateTopicsOptions { OperationTimeout = TimeSpan.FromSeconds(30) });
            logger.LogInformation(
                "Edict.Kafka provisioned topic {Topic} with {Partitions} partitions, rf={Rf}",
                spec.Name, spec.NumPartitions, effectiveReplicationFactor);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == KafkaErrorCode.TopicAlreadyExists))
        {
            logger.LogInformation("Edict.Kafka topic {Topic} already exists", spec.Name);
        }
    }

    static short ResolveReplicationFactor(
        short requested,
        bool isExplicit,
        int brokerCount,
        string topicName)
    {
        if (requested <= brokerCount)
        {
            return requested;
        }
        if (isExplicit)
        {
            throw new InvalidOperationException(
                $"Edict.Kafka cannot provision topic '{topicName}' with the explicitly requested replication factor {requested}: only {brokerCount} broker(s) are available. Either provision more brokers or omit EdictKafkaStreamsOptions.ReplicationFactor to let the provisioner auto-clamp.");
        }
        return (short)brokerCount;
    }
}
