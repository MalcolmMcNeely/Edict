using Edict.Contracts.Configuration;
using Edict.Core.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Configuration;

// EdictWiringValidator is the IHostedService that fails fast at StartAsync:
// missing-provider call + invalid-option value both accumulate into ONE
// InvalidOperationException so a consumer who has two problems sees two
// problems (PRD user story #6). The aggregated message itself is the
// assertion — drift in wording fails CI on the snapshot diff.
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

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

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
                services.AddHostedService<EdictWiringValidator>();
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
