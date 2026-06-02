using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests.Saga;

// In-memory TestCluster for saga lifecycle: memory streams, reminders, and
// grain storage — no Azurite, no Edict.Testing. A static FakeTimeProvider is
// the silo's clock so a test arms a cap then advances past it deterministically;
// events are driven through the in-process IEdictEventConsumer seam rather than
// the pulling agent, so a frozen-then-advanced clock never stalls delivery.
public sealed class SagaLifecycleClusterFixture : IAsyncLifetime
{
    // Started at a realistic instant (not epoch) so the memory-stream subsystem
    // never sees a zero clock; advanced forward only.
    public static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero));

    public TestCluster Cluster { get; private set; } = null!;

    public IGrainFactory GrainFactory => Cluster.GrainFactory;

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
            .AddAssembly(typeof(DefaultCapSaga).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Register the virtual clock before AddEdictOutbox so its
            // TryAddSingleton(TimeProvider.System) is skipped.
            siloBuilder.Services.AddSingleton<TimeProvider>(Time);
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();

            // Swap the real publish executor for the capturing one (Replace, not
            // append — the host keys executors by Kind, so a duplicate would throw).
            var publishDescriptor = siloBuilder.Services.Single(descriptor =>
                descriptor.ServiceType == typeof(IOutboxEffectExecutor)
                && descriptor.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publishDescriptor);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, CapturingPublishEventExecutor>();

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
