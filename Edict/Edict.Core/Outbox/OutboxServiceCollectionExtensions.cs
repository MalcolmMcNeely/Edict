using Edict.Contracts.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Edict.Core.Outbox;

/// <summary>
/// Silo-side wiring for the Outbox engine: the drain engine, the per-kind
/// effect executors, the consumer-tunable <see cref="EdictOutboxOptions"/>, and
/// the <see cref="TimeProvider"/> clock seam (ADR 0018 / 0019). The shipped
/// in-memory Test Framework substitutes a virtual clock by registering its own
/// <see cref="TimeProvider"/> before this call.
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
        services.AddSingleton<IOutboxEffectExecutor, UpsertRowExecutor>();
        services.AddSingleton<OutboxDrainEngine>();
        return services;
    }
}
