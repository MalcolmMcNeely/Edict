using Azure.Storage.Queues;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Hosting;

namespace Edict.Azure.Tests.Configuration;

// Wiring fact for the NumQueues option. The HostBuilder.UseOrleans path
// matches Edict.Kafka.Tests/Configuration/EdictKafkaSiloBuilderExtensionsTests
// — silo registrations resolve through the host's IOptionsMonitor without a
// full TestCluster bring-up, since the assertion target is the option-flow
// from EdictAzureStreamsOptions into Orleans' AzureQueueOptions.
public sealed class AddEdictAzureStreamsNumQueuesTests
{
    static readonly QueueServiceClient FakeQueueClient =
        new("UseDevelopmentStorage=true");

    [Fact]
    public void NumQueues_ShouldDefaultTo16_OnAzureQueueOptions()
    {
        using var host = new HostBuilder()
            .UseOrleans(silo => silo
                .UseLocalhostClustering()
                .AddEdictAzureStreams(o => o.QueueServiceClient = FakeQueueClient))
            .Build();

        var monitor = host.Services.GetRequiredService<IOptionsMonitor<AzureQueueOptions>>();
        Assert.Equal(16, monitor.Get("edict").QueueNames.Count);
    }

    [Fact]
    public void NumQueues_ShouldFlowConfiguredValue_ToAzureQueueOptions()
    {
        using var host = new HostBuilder()
            .UseOrleans(silo => silo
                .UseLocalhostClustering()
                .AddEdictAzureStreams(o =>
                {
                    o.QueueServiceClient = FakeQueueClient;
                    o.NumQueues = 4;
                }))
            .Build();

        var monitor = host.Services.GetRequiredService<IOptionsMonitor<AzureQueueOptions>>();
        Assert.Equal(4, monitor.Get("edict").QueueNames.Count);
    }
}
