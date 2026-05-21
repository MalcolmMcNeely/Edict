using Edict.Contracts.Configuration;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.ClaimCheck;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests.Idempotency;

/// <summary>
/// Dedicated cluster that configures
/// <see cref="EdictOptions.IdempotencyWindowSize"/> to a non-default value
/// so the override-vs-default behaviour of <c>EdictIdempotencyBase.WindowSize</c>
/// is observable without polluting the shared <see cref="EdictClusterFixture"/>
/// (whose other consumers all override <c>WindowSize</c> for unrelated reasons).
/// </summary>
public sealed class WindowSizeClusterFixture : IAsyncLifetime
{
    public const int ConfiguredWindowSize = 2;

    public TestCluster Cluster { get; private set; } = null!;

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
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(new InMemoryTableStoreFactory());
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(new InMemoryClaimCheckStore());
            siloBuilder.Services.AddOptions<EdictOptions>().Configure(o =>
            {
                o.IdempotencyWindowSize = ConfiguredWindowSize;
            });
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
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
