using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.Outbox;
using Edict.Generated;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests;

/// <summary>
/// In-memory cluster tuned to dead-letter on the very first failure and cap the
/// DeadLetter slice at one entry, so a test can drive a grain into the
/// block-intake state deterministically (ADR 0019). Reuses the flippable
/// <see cref="ControllableOutboxExecutor"/> and a virtual clock.
/// </summary>
public sealed class DeadLetterCapClusterFixture : IAsyncLifetime
{
    static readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));

    public TestCluster Cluster { get; private set; } = null!;

    public FakeTimeProvider Clock => _clock;

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

    public async Task DisposeAsync()
    {
        if (Cluster is not null)
            await Cluster.DisposeAsync();
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton<TimeProvider>(_clock);
            siloBuilder.Services.AddSingleton(new EdictOutboxOptions
            {
                MaxAttempts = 1,
                DeadLetterCap = 1,
                JitterFraction = 0,
                BaseDelay = TimeSpan.FromSeconds(1),
            });
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ControllableOutboxExecutor>();
            siloBuilder.Services.AddSingleton<OutboxDrainEngine>();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

// One collection for every test that flips the process-wide
// ControllableOutboxExecutor static. xUnit serialises a single collection, so
// the OutboxRecovery and DeadLetterCap fixtures never drive the shared
// failure flag concurrently (cross-collection parallelism would race it).
[CollectionDefinition(Name)]
public sealed class DeadLetterCapClusterCollection
    : ICollectionFixture<DeadLetterCapClusterFixture>,
      ICollectionFixture<OutboxRecoveryClusterFixture>
{
    public const string Name = "ControllableOutboxCluster";
}
