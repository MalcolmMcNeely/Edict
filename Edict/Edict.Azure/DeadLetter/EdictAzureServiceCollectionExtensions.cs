using Azure.Data.Tables;

using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.DeadLetter;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.DeadLetter;

/// <summary>
/// DI front door for the Azure provider's dead-letter wiring (ADR 0022). The
/// consumer-facing <see cref="IEdictDeadLetterRepository"/> facade is registered
/// by <c>Edict.Core</c>'s <c>AddEdict()</c>; this extension supplies the Azure-
/// backed <see cref="IEdictTableRepository{T}"/> seam that facade resolves so
/// reads land on the <c>"deadletter"</c> partition the framework's projection
/// writes to. Brand-prefixed because <c>EdictAzure</c> is the consumer-typed
/// surface for the provider's DI registration helpers (CONTEXT.md clause a).
/// </summary>
public static class EdictAzureServiceCollectionExtensions
{
    /// <summary>
    /// Plugs <see cref="AzureTableRepository{T}"/> over the singleton dead-letter
    /// table as <see cref="IEdictTableRepository{T}"/>, so the auto-wired
    /// <see cref="IEdictDeadLetterRepository"/> reads through the same Azurite/
    /// Azure Table Storage backend that the framework projection writes to. The
    /// <see cref="TableServiceClient"/> is resolved from DI — the host is
    /// expected to have registered one for the Azure write-store factory.
    /// </summary>
    public static IServiceCollection AddEdictAzureDeadLetterRepository(this IServiceCollection services)
    {
        services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(sp =>
            new AzureTableRepository<EdictDeadLetterEntry>(
                sp.GetRequiredService<TableServiceClient>(),
                EdictDeadLetterProjectionBuilder.DeadLetterPartition));
        return services;
    }
}
