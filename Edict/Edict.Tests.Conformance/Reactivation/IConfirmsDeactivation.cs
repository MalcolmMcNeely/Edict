using Orleans;

namespace Edict.Tests.Conformance.Reactivation;

// The deactivate-and-confirm seam: a probe a Guid-keyed grain implements so a
// scenario can force it to deactivate and block until the activation is genuinely
// torn down — proven by a fresh activation answering with a different id. Not
// Task.Delay-bridgeable: Orleans teardown is not gated by the injected TimeProvider,
// so a scenario that needs a real reactivation drives it through DeactivationWaiter
// rather than guessing at a wall-clock pause. The grain stamps a fresh id in
// OnActivateAsync; the waiter polls GetActivationIdAsync until it changes.
public interface IConfirmsDeactivation : IGrainWithGuidKey
{
    Task<Guid> GetActivationIdAsync();

    Task DeactivateAsync();
}
