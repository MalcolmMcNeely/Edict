using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;

namespace Edict.Azure.Tests.Idempotency;

public interface IIdempotencyWindowSizeDefaultProbe : IGrainWithGuidKey
{
    Task<int> GetEffectiveWindowSizeAsync();
}

public interface IIdempotencyWindowSizeOverrideProbe : IGrainWithGuidKey
{
    Task<int> GetEffectiveWindowSizeAsync();
}

/// <summary>
/// Non-overriding consumer: <c>WindowSize</c> resolves to the silo-wide
/// default fed by
/// <see cref="Contracts.Configuration.EdictOptions.IdempotencyWindowSize"/>.
/// Proves the option threads through DI to the base when no subclass override
/// is declared.
/// </summary>
public sealed class IdempotencyWindowSizeDefaultProbe : EdictIdempotencyBase, IIdempotencyWindowSizeDefaultProbe
{
    protected override Task<bool> DispatchAsync(EdictEvent evt) => Task.FromResult(false);

    public Task<int> GetEffectiveWindowSizeAsync() => Task.FromResult(WindowSize);
}

/// <summary>
/// Overriding consumer: a specific subclass picks its own <c>WindowSize</c>.
/// The per-type override must beat the silo-wide default (a high-throughput
/// singleton typically needs a much larger window than the default).
/// </summary>
public sealed class IdempotencyWindowSizeOverrideProbe : EdictIdempotencyBase, IIdempotencyWindowSizeOverrideProbe
{
    protected override int WindowSize => 7;

    protected override Task<bool> DispatchAsync(EdictEvent evt) => Task.FromResult(false);

    public Task<int> GetEffectiveWindowSizeAsync() => Task.FromResult(WindowSize);
}
