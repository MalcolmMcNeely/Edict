using Edict.Core.Grains;
using Edict.Core.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;
using Sample.Silo.Orders;
using Xunit;

namespace Sample.Api.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var clusterBuilder = new TestClusterBuilder();
        clusterBuilder.AddSiloBuilderConfigurator<SiloConfigurator>();
        clusterBuilder.AddClientBuilderConfigurator<ClientConfigurator>();
        _cluster = clusterBuilder.Build();
        await _cluster.DeployAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Testing");
                host.ConfigureServices(services =>
                {
                    services.AddSingleton<IClusterClient>(_cluster.Client);
                    services.AddSingleton<IGrainFactory>(_cluster.Client);
                });
            });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    private static void ConfigureEdictSerialization(ISerializerBuilder ser)
    {
        ser.AddAssembly(typeof(OrderGrain).Assembly);
        ser.AddAssembly(typeof(IEdictCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) =>
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
    }
}
