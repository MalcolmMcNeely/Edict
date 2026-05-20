using Edict.Contracts.Configuration;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests.Outbox;

/// <summary>
/// In-memory cluster scoped to the <see cref="TestDurableConsumer"/> host
/// plumbing tests: registers <see cref="CountingExecutor"/>s for the three
/// effect kinds so the drain engine records what it would have published, with
/// a one-entry dead-letter cap and a MaxAttempts=1 policy so block-intake and
/// dead-letter scenarios are deterministic. No streams, no commands — only the
/// host plumbing under test.
/// </summary>
public sealed class TestDurableConsumerClusterFixture : IAsyncLifetime
{
    static readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));

    public TestCluster Cluster { get; private set; } = null!;

    public FakeTimeProvider Clock => _clock;

    public IGrainFactory GrainFactory => Cluster.GrainFactory;

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
        {
            await Cluster.DisposeAsync();
        }
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(TestDurableConsumer).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton<TimeProvider>(_clock);
            siloBuilder.Services.AddSingleton(new EdictOutboxOptions
            {
                MaxAttempts = 1,
                JitterFraction = 0,
                BaseDelay = TimeSpan.FromSeconds(1),
            });
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(_ => new CountingExecutor(OutboxEffectKind.PublishEvent));
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(_ => new CountingExecutor(OutboxEffectKind.SendCommand));
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(_ => new CountingExecutor(OutboxEffectKind.UpsertRow));
            siloBuilder.Services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
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
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
        }
    }
}
