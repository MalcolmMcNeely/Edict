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
/// <see cref="TimeProvider"/> clock seam, and the dead-letter promoter
/// (ADR 0018 / 0022). The shipped in-memory Test Framework substitutes a
/// virtual clock by registering its own <see cref="TimeProvider"/> before this
/// call.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddEdictOutbox(
        this IServiceCollection services,
        Action<EdictOutboxOptions>? configure = null)
    {
        var options = new EdictOutboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOutboxEffectExecutor, PublishEventExecutor>();
        services.AddSingleton<IOutboxEffectExecutor, SendCommandExecutor>();
        services.AddSingleton<IOutboxEffectExecutor, UpsertRowExecutor>();
        services.AddSingleton<IOutboxEffectExecutor, InvokeHandlerExecutor>();
        services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
        // Default claim-check policy: threshold is int.MaxValue so the
        // commit pipeline never trips into the pointer branch and the absent
        // IEdictClaimCheckStore is never queried. The Azure provider and the
        // shipped Test Framework each replace this with their own policy +
        // store registration (ADR 0024).
        services.TryAddSingleton(sp => new ClaimCheckPolicy(
            sp.GetRequiredService<Serializer>(),
            thresholdBytes: int.MaxValue,
            store: sp.GetService<IEdictClaimCheckStore>()));
        return services;
    }
}
