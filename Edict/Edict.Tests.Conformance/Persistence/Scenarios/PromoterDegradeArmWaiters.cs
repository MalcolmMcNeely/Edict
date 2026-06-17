using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.DeadLetter;

namespace Edict.Tests.Conformance.Persistence;

// Positive-signal waiter for the promoted forensic row. The promoter publishes
// the synthetic dead-letter row to the dead-letter stream, which the projection
// writes into the real table asynchronously off the drain turn — so the scenario
// polls the store for the row rather than gating on a clock. Lives outside the
// *Scenarios surface so the paced store reads stay clear of the no-Task.Delay
// scenario doctrine; the deadline is a liveness backstop, not a timing assertion.
static class PromoterDegradeArmWaiters
{
    public static async Task<EdictDeadLetterEntry> WaitForDeadLetterRowAsync(
        IEdictTableWriteStore<EdictDeadLetterEntry> deadLetterTable, string sourceGrainKeyFragment)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            var match = entries.FirstOrDefault(entry => entry.SourceGrainKey.Contains(sourceGrainKeyFragment));
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException(
            $"Promoted dead-letter row for source grain '{sourceGrainKeyFragment}' never landed within 30 seconds.");
    }
}
