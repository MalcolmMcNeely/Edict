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
    readonly ILogger<EdictKafkaTopicProvisioner> _logger;

    public EdictKafkaTopicProvisioner(
        EdictKafkaStreamsOptions options,
        ILogger<EdictKafkaTopicProvisioner> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var adminConfig = new AdminClientConfig { BootstrapServers = _options.BootstrapServers };
        using var admin = new AdminClientBuilder(adminConfig).Build();
        var spec = new TopicSpecification
        {
            Name = EdictKafkaAdapter.TopicName,
            NumPartitions = _options.PartitionCount,
            ReplicationFactor = 1,
        };
        try
        {
            await admin.CreateTopicsAsync(
                [spec],
                new CreateTopicsOptions { OperationTimeout = TimeSpan.FromSeconds(30) });
            _logger.LogInformation(
                "Edict.Kafka provisioned topic {Topic} with {Partitions} partitions",
                spec.Name, spec.NumPartitions);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == KafkaErrorCode.TopicAlreadyExists))
        {
            _logger.LogInformation("Edict.Kafka topic {Topic} already exists", spec.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Edict.Kafka topic provisioning failed for {Topic}", spec.Name);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
