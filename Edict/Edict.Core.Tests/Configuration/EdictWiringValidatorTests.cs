using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Core.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

public sealed class EdictWiringValidatorTests
{
    [Fact]
    public async Task StartAsync_ShouldThrowAggregatedFailure_WhenProvidersMissingAndOptionsInvalid()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<EdictOptions>().Configure(o =>
                {
                    o.OutboxJitterFraction = 1.5;
                    o.OutboxMaxAttempts = 0;
                });
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        var failure = await Assert.ThrowsAsync<EdictWiringException>(() => host.StartAsync());

        await Verify(failure.Message);
    }

    [Fact]
    public async Task StartAsync_ShouldComplete_WhenAllProvidersAreRegisteredAndOptionsAreValid()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<EdictOptions>();
                services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
                services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
                services.AddSingleton<IEdictClaimCheckStore, NullClaimCheckStore>();
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenStreamsRegisteredButClaimCheckStoreMissing()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<EdictOptions>();
                services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
                services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        var failure = await Assert.ThrowsAsync<EdictWiringException>(() => host.StartAsync());

        Assert.Contains("IEdictClaimCheckStore", failure.Message);
    }

    sealed class NullClaimCheckStore : IEdictClaimCheckStore
    {
        public Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            Task.FromResult("test");

        public Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
    }
}
