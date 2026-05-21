using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests;

/// <summary>
/// In-memory cluster wired with a flippable <see cref="ControllableOutboxExecutor"/>
/// and a virtual clock, so tests can drive a post-commit publish failure and a
/// recovery (drain-on-activation) under a deterministic backoff (ADR 0016/0018).
/// </summary>
public sealed class OutboxRecoveryClusterFixture : IAsyncLifetime
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
            siloBuilder.Services.AddOptions<EdictOptions>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ControllableOutboxExecutor>();
            siloBuilder.Services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
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

// The OutboxRecovery + DeadLetterCap tests share one xUnit collection
// (DeadLetterCapClusterCollection) so the process-wide
// ControllableOutboxExecutor static is never raced across parallel
// collections. This fixture is contributed there as a second collection
// fixture; it intentionally no longer defines its own collection.
