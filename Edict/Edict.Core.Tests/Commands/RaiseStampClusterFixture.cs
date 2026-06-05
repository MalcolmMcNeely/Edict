using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.Grains;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.Commands;

[CollectionDefinition(Name)]
public sealed class RaiseStampCollection : ICollectionFixture<RaiseStampClusterFixture>
{
    public const string Name = "RaiseStamp";
}

// In-memory cluster whose clock is frozen, so an event's OccurredAt is exactly
// the instant the handler raised it. The real publish executor is swapped for
// one that records the live wire event off the outbox seam, so the stamp is read
// synchronously on the command turn rather than chased through async stream
// delivery.
public sealed class RaiseStampClusterFixture : IAsyncLifetime
{
    public static readonly DateTimeOffset FrozenNow = new(2026, 5, 25, 10, 0, 0, TimeSpan.Zero);

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() =>
        Cluster is not null ? Cluster.DisposeAsync().AsTask() : Task.CompletedTask;

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Register the frozen clock before AddEdictOutbox so its
            // TryAddSingleton(TimeProvider.System) is skipped.
            siloBuilder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(FrozenNow));
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();

            // Swap the real publish executor for the capturing one (Replace, not
            // append — the host keys executors by Kind, so a duplicate would throw).
            var publishDescriptor = siloBuilder.Services.Single(descriptor =>
                descriptor.ServiceType == typeof(IOutboxEffectExecutor)
                && descriptor.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publishDescriptor);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, RaiseStampCapturingExecutor>();

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}
