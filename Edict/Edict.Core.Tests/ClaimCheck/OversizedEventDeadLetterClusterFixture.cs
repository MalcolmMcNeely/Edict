using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.DeadLetter;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.TableStorage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests.ClaimCheck;

// ADR 0024 slice 4: in-memory cluster that wires a low-threshold claim-check
// policy + an in-memory blob store alongside the dead-letter end-to-end loop
// from ADR 0022. A raised event commits as a pointer-bearing
// EdictEventEnvelope on the Outbox; the poisoned publish executor drives the
// promotion path; the engine promotes the envelope-payload entry into an
// EdictDeadLetterRaised carrying ClaimCheckKey with no inline body — the
// forensic row never tries to fit the >threshold body into the Azure Table
// property the projection writes.
public sealed class OversizedEventDeadLetterClusterFixture : IAsyncLifetime
{
    static readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));
    static readonly InMemoryTableStoreFactory _tableStoreFactory = new();
    static readonly InMemoryClaimCheckStore _claimCheckStore = new();

    public TestCluster Cluster { get; private set; } = null!;
    public FakeTimeProvider Clock => _clock;
    public InMemoryTableStoreFactory TableStoreFactory => _tableStoreFactory;
    public InMemoryClaimCheckStore ClaimCheckStore => _claimCheckStore;
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
            // Force every raised event through the pointer-branch so the
            // failing entry's payload is a pointer-bearing EdictEventEnvelope —
            // the case slice 4 closes for the forensic projection.
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(_claimCheckStore);
            siloBuilder.Services.AddSingleton(sp => new ClaimCheckPolicy(
                sp.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: sp.GetService<IEdictClaimCheckStore>()));
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, DeadLetterAwarePublishExecutor>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, SendCommandExecutor>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, UpsertRowExecutor>();
            siloBuilder.Services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
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
