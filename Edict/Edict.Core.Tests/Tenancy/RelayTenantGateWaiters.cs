using Edict.Contracts.DeadLetter;

namespace Edict.Core.Tests.Tenancy;

// Polls the in-process capture sinks for the relayed-gate cluster tests. The
// scenarios assert order-independent outcomes; the waiting lives here so no test
// body sleeps on a fixed delay.
static class RelayTenantGateWaiters
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static Task<EdictDeadLetterRaised?> WaitForDeadLetterAsync(Func<EdictDeadLetterRaised, bool> matches) =>
        PollAsync(() => RelayTenantGateCaptures.DeadLetters.FirstOrDefault(matches));

    public static Task<DispatchIntoWalledTarget?> WaitForWalledTargetAsync(Guid workflowId) =>
        PollAsync(() => RelayTenantGateCaptures.WalledTargetReceived.FirstOrDefault(command => command.TargetId.Value == workflowId));

    public static Task<DispatchIntoPublicTarget?> WaitForPublicTargetAsync(Guid workflowId) =>
        PollAsync(() => RelayTenantGateCaptures.PublicTargetReceived.FirstOrDefault(command => command.TargetId == workflowId));

    static async Task<T?> PollAsync<T>(Func<T?> probe) where T : class
    {
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (probe() is { } match)
            {
                return match;
            }
            await Task.Delay(PollInterval);
        }
        return probe();
    }
}
