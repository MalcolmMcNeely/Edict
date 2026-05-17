using Edict.Core.Serialization;
using Edict.Generated;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Core.Tests;

/// <summary>
/// A real in-memory Orleans cluster. This slice has no streams, so a plain
/// TestCluster (no Azurite) is the right tool — Azurite/Testcontainers enters
/// with the event-stream slice. The client gets the generated
/// <c>AddEdict()</c>, so a test resolves <see cref="IEdictSender"/> exactly as
/// a consumer would.
/// </summary>
public sealed class EdictClusterFixture : IAsyncLifetime
{
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

    public async Task DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
    }

    // Both hosts must see the Orleans metadata generated into the sample
    // (test) assembly and Edict.Core — TestCluster doesn't discover them by
    // default — and route the Edict contract types through MessagePack
    // (ADR 0007, superseding ADR 0006's JSON).
    private static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderGrain).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) =>
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}
