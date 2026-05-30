using System.ComponentModel;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.EventHandler;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Serialization;

namespace Edict.Core.Outbox;

/// <summary>
/// Silo-side wiring for the Outbox host: the per-kind effect executors, the
/// consumer-tunable <see cref="EdictOutboxOptions"/>, the
/// <see cref="TimeProvider"/> clock seam, and the dead-letter promoter.
/// The shipped in-memory Test Framework substitutes a
/// virtual clock by registering its own <see cref="TimeProvider"/> before this
/// call.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddEdictOutbox(
        this IServiceCollection services,
        Action<EdictOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.AddOptions<EdictOptions>().Configure(configure);
        }
        else
        {
            services.AddOptions<EdictOptions>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RowTypeResolver>();
        services.AddSingleton<IOutboxEffectExecutor, PublishEventExecutor>();
        services.AddSingleton<IOutboxEffectExecutor, SendCommandExecutor>();
        services.AddSingleton<IOutboxEffectExecutor, UpsertRowExecutor>();
        services.AddSingleton<IOutboxEffectExecutor, InvokeHandlerExecutor>();
        services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
        // Default claim-check policy: threshold is int.MaxValue so the
        // commit pipeline never trips into the pointer branch and the absent
        // IEdictClaimCheckStore is never queried. The Azure provider and the
        // shipped Test Framework each replace this with their own policy +
        // store registration.
        services.TryAddSingleton(serviceProvider => new ClaimCheckPolicy(
            serviceProvider.GetRequiredService<Serializer>(),
            thresholdBytes: int.MaxValue,
            store: serviceProvider.GetService<IEdictClaimCheckStore>(),
            accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));
        // InvokeHandlerExecutor calls ClaimCheckUnwrap.ApplyAsync before
        // dispatch. Re-registering with TryAddSingleton means the
        // dead-letter-projection-aware variant from AddEdict() wins when both
        // are wired; hosts that opt into AddEdictOutbox alone (the Telemetry
        // tests, for example) get a default that fetches for every consumer.
        services.TryAddSingleton(serviceProvider => new ClaimCheckUnwrap(
            serviceProvider.GetRequiredService<Serializer>(),
            serviceProvider.GetService<IEdictClaimCheckStore>()));
        return services;
    }
}
