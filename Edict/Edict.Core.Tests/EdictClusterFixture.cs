using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.TableStorage;
using Edict.Generated;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests;

/// <summary>
/// In-memory Orleans cluster for provider-independent mechanism tests (ADR 0016).
/// Uses Orleans memory streams and in-memory grain storage — no Testcontainers.
/// The table write store is the <see cref="InMemoryTableStoreFactory"/> fake.
/// </summary>
public sealed class EdictClusterFixture : IAsyncLifetime
{
    private static readonly InMemoryTableStoreFactory _tableStoreFactory = new();

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public InMemoryTableStoreFactory TableStoreFactory => _tableStoreFactory;

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

    private static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton<IValidator<ValidateSkuCommand>, SkuRequiredValidator>();
            siloBuilder.Services.AddSingleton<IValidator<StateCheckCommand>, GrainStateRequiredValidator>();
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(_tableStoreFactory);
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-dedup");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
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
