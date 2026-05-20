using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.TableStorage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests.DeadLetter;

// In-memory cluster wired for the dead-letter end-to-end loop (ADR 0022):
// a fail-on-non-dead-letter publish executor drives the promotion path, a
// FakeTimeProvider lets the test step past the backoff gates, and AddEdict()
// auto-wires the IEdictDeadLetterRepository facade over the in-memory
// IEdictTableRepository<EdictDeadLetterEntry>. The framework-shipped
// EdictDeadLetterProjectionBuilder grain is discovered by Orleans via the
// Edict.Core assembly reference; the implicit subscription on the
// "edict-dead-letter" stream binds delivery without any test-level wiring.
public sealed class DeadLetterPromoteClusterFixture : IAsyncLifetime
{
    static readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));
    static readonly InMemoryTableStoreFactory _tableStoreFactory = new();

    public TestCluster Cluster { get; private set; } = null!;
    public FakeTimeProvider Clock => _clock;
    public InMemoryTableStoreFactory TableStoreFactory => _tableStoreFactory;
    public IEdictSender Sender => Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
    public IEdictDeadLetterRepository DeadLetterRepository =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictDeadLetterRepository>();

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
            // MaxAttempts kept small so the test promotes after a handful of
            // force-drain cycles rather than the default 8.
            siloBuilder.Services.AddSingleton(new EdictOutboxOptions
            {
                MaxAttempts = 3,
                BaseDelay = TimeSpan.FromSeconds(2),
                JitterFraction = 0,
            });
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(_tableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                _tableStoreFactory.GetOrCreateStore<EdictDeadLetterEntry>(
                    EdictDeadLetterProjectionBuilder.DeadLetterPartition));
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, DeadLetterAwarePublishExecutor>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, SendCommandExecutor>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, UpsertRowExecutor>();
            siloBuilder.Services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
            siloBuilder.Services.AddSingleton<OutboxDrainEngine>();
            siloBuilder.Services.AddEdict();
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
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                _tableStoreFactory.GetOrCreateStore<EdictDeadLetterEntry>(
                    EdictDeadLetterProjectionBuilder.DeadLetterPartition));
            clientBuilder.Services.AddEdict();
        }
    }
}
