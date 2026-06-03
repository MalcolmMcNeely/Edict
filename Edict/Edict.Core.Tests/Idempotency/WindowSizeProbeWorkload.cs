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

// Non-overriding consumer: WindowSize resolves to the silo-wide default fed by
// EdictOptions.IdempotencyWindowSize, proving the option threads through DI to
// the base when no subclass override is declared.
public sealed class WindowSizeDefaultProbe : EdictIdempotencyBase, IWindowSizeDefaultProbe
{
    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent) => Task.FromResult(EdictDispatchOutcome.NotHandled);

    public Task<int> GetEffectiveWindowSizeAsync() => Task.FromResult(WindowSize);
}

// Overriding consumer: a specific subclass picks its own WindowSize, which must
// beat the silo-wide default.
public sealed class WindowSizeOverrideProbe : EdictIdempotencyBase, IWindowSizeOverrideProbe
{
    protected override int WindowSize => 7;

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent) => Task.FromResult(EdictDispatchOutcome.NotHandled);

    public Task<int> GetEffectiveWindowSizeAsync() => Task.FromResult(WindowSize);
}
