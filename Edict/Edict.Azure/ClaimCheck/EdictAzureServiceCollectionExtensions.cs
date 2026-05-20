using Azure.Storage.Blobs;

using Edict.Contracts.ClaimCheck;
using Edict.Core.ClaimCheck;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

namespace Edict.Azure.ClaimCheck;

/// <summary>
/// DI front door for the Azure provider's claim-check wiring (ADR 0024).
/// Registers the blob-backed <see cref="IEdictClaimCheckStore"/> and the
/// <see cref="ClaimCheckPolicy"/> seam so the Outbox commit boundary routes
/// oversized events through Azure Blob storage instead of the in-memory or
/// no-op default policy. Brand-prefixed because <c>EdictAzure</c> is the
/// consumer-typed surface for the provider's DI registration helpers
/// (CONTEXT.md clause a).
/// </summary>
public static class EdictAzureClaimCheckServiceCollectionExtensions
{
    /// <summary>
    /// Plugs <see cref="AzureBlobClaimCheckStore"/> and a tuned
    /// <see cref="ClaimCheckPolicy"/> into DI. The
    /// <see cref="BlobServiceClient"/> is resolved from DI — the host is
    /// expected to have registered one (the AppHost typically wires it
    /// alongside the table + queue clients).
    /// </summary>
    public static IServiceCollection AddEdictAzureClaimCheck(
        this IServiceCollection services,
        Action<EdictAzureOptions>? configure = null)
    {
        var options = new EdictAzureOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddSingleton<IEdictClaimCheckStore>(sp =>
            AzureBlobClaimCheckStore
                .CreateAsync(
                    sp.GetRequiredService<BlobServiceClient>(),
                    options.ClaimCheckContainerName)
                .GetAwaiter().GetResult());

        services.AddSingleton(sp => new ClaimCheckPolicy(
            sp.GetRequiredService<Serializer>(),
            options.ClaimCheckThresholdBytes,
            sp.GetRequiredService<IEdictClaimCheckStore>()));

        return services;
    }
}
