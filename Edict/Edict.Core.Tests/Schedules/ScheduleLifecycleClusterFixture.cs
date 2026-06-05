using Edict.Contracts.ClaimCheck;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.Tests.ClaimCheck;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Core.Tests.Schedules;

// An in-memory cluster whose framework clock is a FakeTimeProvider, so a test can
// advance virtual time to a schedule's due instant and drive the grain's fire
// seam deterministically — no wall-clock wait. Memory streams, memory grain
// storage, in-memory reminders; the real outbox/schedule engine runs over them.
[CollectionDefinition(Name)]
public sealed class ScheduleLifecycleCollection : ICollectionFixture<ScheduleLifecycleClusterFixture>
{
    public const string Name = "ScheduleLifecycle";
}

public sealed class ScheduleLifecycleClusterFixture : IAsyncLifetime
{
    static FakeTimeProvider _clock = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public IGrainFactory GrainFactory => Cluster.GrainFactory;

    public void AdvanceClock(TimeSpan by) => _clock.Advance(by);

    public async Task InitializeAsync()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // The virtual clock must win over AddEdictOutbox's
            // TryAddSingleton(TimeProvider.System) — register it first.
            siloBuilder.Services.AddSingleton<TimeProvider>(_clock);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(new InMemoryClaimCheckStore());
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();

            // Swap the real publish executor for the capturing one (Replace, not
            // append — the host keys executors by Kind, so a duplicate would throw)
            // so a schedule-fired event's stamped correlation is readable on the
            // command turn rather than chased through async stream delivery.
            var publishDescriptor = siloBuilder.Services.Single(descriptor =>
                descriptor.ServiceType == typeof(IOutboxEffectExecutor)
                && descriptor.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publishDescriptor);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ScheduleRaiseCapturingExecutor>();

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
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}
