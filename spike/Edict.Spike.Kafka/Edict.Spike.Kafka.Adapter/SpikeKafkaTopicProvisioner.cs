using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikeKafkaTopicProvisioner : IHostedService
{
    readonly SpikeKafkaStreamOptions _options;
    readonly ILogger<SpikeKafkaTopicProvisioner> _logger;

    public SpikeKafkaTopicProvisioner(IOptionsMonitor<SpikeKafkaStreamOptions> options, ILogger<SpikeKafkaTopicProvisioner> logger)
    {
        _options = options.Get(Contracts.SpikeStreamNames.StreamProvider);
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = new AdminClientConfig { BootstrapServers = _options.BootstrapServers };
        using var admin = new AdminClientBuilder(config).Build();
        var spec = new TopicSpecification
        {
            Name = _options.Topic,
            NumPartitions = _options.PartitionCount,
            ReplicationFactor = 1,
        };
        try
        {
            await admin.CreateTopicsAsync(new[] { spec }, new CreateTopicsOptions { OperationTimeout = TimeSpan.FromSeconds(30) });
            _logger.LogInformation("Spike provisioned topic {Topic} with {Partitions} partitions", _options.Topic, _options.PartitionCount);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == Confluent.Kafka.ErrorCode.TopicAlreadyExists))
        {
            _logger.LogInformation("Spike topic {Topic} already exists", _options.Topic);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spike topic provisioning failed for {Topic}; producer may auto-create with broker defaults", _options.Topic);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
