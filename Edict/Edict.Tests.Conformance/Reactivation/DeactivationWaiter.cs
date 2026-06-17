namespace Edict.Tests.Conformance.Reactivation;

// Drives the IConfirmsDeactivation seam: requests deactivation and returns only once
// a fresh activation has answered with a different id, so a caller knows the grain
// genuinely tore down and came back from durable storage. The poll loop lives here,
// not in a *Scenarios.cs file, so a scenario asserts the outcome while the waiter polls.
static class DeactivationWaiter
{
    public static async Task DeactivateAndConfirmAsync(IConfirmsDeactivation grain, int timeoutSeconds = 30)
    {
        var activationBeforeDeactivation = await grain.GetActivationIdAsync();
        await grain.DeactivateAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await grain.GetActivationIdAsync() != activationBeforeDeactivation)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException(
            $"Grain did not deactivate and reactivate within {timeoutSeconds}s; the activation id never changed.");
    }
}
