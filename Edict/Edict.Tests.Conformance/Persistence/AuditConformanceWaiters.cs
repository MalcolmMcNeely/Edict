using System.Diagnostics;

using Edict.Contracts.Audit;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

// Shared polling for the audit scenarios: capture is synchronous-durable into the
// grain-state write but the drain to the backing store is asynchronous, so a
// scenario waits for the expected record count to land before asserting on it.
static class AuditConformanceWaiters
{
    public static async Task<IReadOnlyList<EdictAuditRecord>> WaitForEntityRecordsAsync(
        IAuditConformanceFixture fixture, string entityType, string entityKey, int expectedCount)
    {
        IReadOnlyList<EdictAuditRecord> records = [];
        await WaitUntilAsync(async () =>
        {
            records = await fixture.ReadEntityAsync(entityType, entityKey);
            return records.Count >= expectedCount;
        });
        Assert.True(records.Count >= expectedCount, $"Expected {expectedCount} audit records for {entityKey}, saw {records.Count}.");
        return records;
    }

    public static async Task<IReadOnlyList<EdictAuditRecord>> WaitForCorrelationRecordsAsync(
        IAuditConformanceFixture fixture, Guid correlationId, int expectedCount)
    {
        IReadOnlyList<EdictAuditRecord> records = [];
        await WaitUntilAsync(async () =>
        {
            records = await fixture.ByCorrelationAsync(correlationId);
            return records.Count >= expectedCount;
        });
        Assert.True(records.Count >= expectedCount, $"Expected {expectedCount} audit records for correlation {correlationId}, saw {records.Count}.");
        return records;
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 30);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(100);
        }
    }
}
