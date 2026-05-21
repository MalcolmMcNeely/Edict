using Edict.Contracts.Events;
using Edict.Core.Idempotency;

using Orleans;

namespace Edict.Core.Tests.Idempotency;

public interface IWindowSizeDefaultProbe : IGrainWithGuidKey
{
    Task<int> GetEffectiveWindowSizeAsync();
}

public interface IWindowSizeOverrideProbe : IGrainWithGuidKey
{
    Task<int> GetEffectiveWindowSizeAsync();
}

/// <summary>
/// Non-overriding consumer: <c>WindowSize</c> resolves to
/// <see cref="Contracts.Configuration.EdictOptions.IdempotencyWindowSize"/>,
/// the silo-wide default. Proves the option threads through DI to the base.
/// </summary>
public sealed class WindowSizeDefaultProbe : EdictIdempotencyBase, IWindowSizeDefaultProbe
{
    protected override Task<bool> DispatchAsync(EdictEvent evt) => Task.FromResult(false);

    public Task<int> GetEffectiveWindowSizeAsync() => Task.FromResult(WindowSize);
}

/// <summary>
/// Overriding consumer: a specific subclass picks its own
/// <c>WindowSize</c>; the per-type override must beat the silo-wide default
/// (a high-throughput singleton needs a much larger window than the default).
/// </summary>
public sealed class WindowSizeOverrideProbe : EdictIdempotencyBase, IWindowSizeOverrideProbe
{
    protected override int WindowSize => 7;

    protected override Task<bool> DispatchAsync(EdictEvent evt) => Task.FromResult(false);

    public Task<int> GetEffectiveWindowSizeAsync() => Task.FromResult(WindowSize);
}
