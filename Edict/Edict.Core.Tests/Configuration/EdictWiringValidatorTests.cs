using Edict.Contracts.Audit;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Core.Audit;
using Edict.Core.Configuration;
using Edict.Core.Tests.Audit;

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
    public async Task StartAsync_ShouldThrow_WhenScheduleDefaultTimeoutIsNegative()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<EdictCommandHandlerScheduleOptions>().Configure(o =>
                {
                    o.DefaultTimeout = TimeSpan.FromMinutes(-5);
                });
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        var failure = await Assert.ThrowsAsync<EdictWiringException>(() => host.StartAsync());

        Assert.Contains(nameof(EdictCommandHandlerScheduleOptions.DefaultTimeout), failure.Message);
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

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenAuditEnabledButNoResolverRegistered()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Stands in for siloBuilder.WithAudit(): the on-switch with no resolver behind it.
                services.AddSingleton<EdictAuditEnabledMarker>();
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        var failure = await Assert.ThrowsAsync<EdictWiringException>(() => host.StartAsync());

        Assert.Contains("AddEdictAudit", failure.Message);
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenAuditCapturingButNoStoreRegistered()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Stands in for siloBuilder.WithAudit() on a silo: capture is on and a
                // resolver is present, but no store was registered to drain records to.
                services.AddSingleton<EdictAuditEnabledMarker>();
                services.AddSingleton<EdictAuditSiloMarker>();
                services.AddEdictAudit(() => EdictPrincipal.Of("alice"));
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        var failure = await Assert.ThrowsAsync<EdictWiringException>(() => host.StartAsync());

        Assert.Contains("audit store", failure.Message);
    }

    [Fact]
    public async Task StartAsync_ShouldThrow_WhenAuditCapturingButNoPayloadStoreRegistered()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                // Capture is on with a chain store, but no payload store to land the
                // captured bodies in.
                services.AddSingleton<EdictAuditEnabledMarker>();
                services.AddSingleton<EdictAuditSiloMarker>();
                services.AddEdictAudit(() => EdictPrincipal.Of("alice"));
                services.AddSingleton<IEdictAuditStore>(new RecordingAuditStore());
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        var failure = await Assert.ThrowsAsync<EdictWiringException>(() => host.StartAsync());

        Assert.Contains("audit payload store", failure.Message);
    }

    [Fact]
    public async Task StartAsync_ShouldComplete_WhenAuditEnabledAndResolverRegistered()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<EdictOptions>();
                services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
                services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
                services.AddSingleton<IEdictClaimCheckStore, NullClaimCheckStore>();
                services.AddSingleton<EdictAuditEnabledMarker>();
                services.AddEdictAudit(() => EdictPrincipal.Of("alice"));
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    sealed class NullClaimCheckStore : IEdictClaimCheckStore
    {
        public Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
    }
}
