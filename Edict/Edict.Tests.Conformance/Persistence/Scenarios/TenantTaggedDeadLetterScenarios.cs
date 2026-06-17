using Edict.Contracts.DeadLetter;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;
using Edict.Telemetry;
using Edict.Tests.Conformance.Tenancy;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Dead-letter is operator-scoped, not tenant-read, but it carries a tenant tag so
/// an operator can filter failures by wall. On a real persistence backend: a
/// tenant-scoped command's publish is poisoned until its outbox entry exhausts its
/// attempts and promotes, and the row that lands in the real dead-letter table
/// carries the tenant recovered from the source grain's own composed key — the
/// static wall, parsed back out, never a per-message field. The drain is driven
/// through the grain's probe; because the grain is tenant-keyed, the probe call is
/// a privileged crossing the harness authorizes explicitly (the operator path),
/// rather than the silent same-tenant path a send takes.
/// </summary>
public abstract class TenantTaggedDeadLetterScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected TenantTaggedDeadLetterScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PoisonedTenantScopedCommand_ShouldLandDeadLetterRowTaggedWithItsTenant()
    {
        var tenant = EdictTenantId.Of("acme");
        var employeeId = new EmployeeId(Guid.NewGuid());
        var composedKey = EdictKeyComposer.Compose(tenant, employeeId.Value.ToString("N"));

        // The drain probe reaches a tenant-keyed command grain directly, so the
        // isolation filter would deny it as a cross-wall reach; authorize it as the
        // operator-driven crossing it is.
        TenantCrossing.Authorize();

        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = true;

        ConformanceTenantSource.Current = tenant;
#pragma warning disable EDICT024
        await _fixture.Sender.SendAsync(new AddEmployeeCommand(employeeId, "Engineering"));
#pragma warning restore EDICT024

        var probe = _fixture.GrainFactory.GetGrain<IEmployeeOutboxProbe>(composedKey);

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return _fixture.OutboxFault.FailedAttempts >= 2;
        });

        // Heal the controllable so the promoted EdictDeadLetterRaised entry can
        // publish — otherwise it would loop on the same fail/promote cycle.
        _fixture.OutboxFault.ShouldFail = false;

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);

        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            return entries.Any(entry => entry.SourceGrainKey == composedKey);
        });

        var allEntries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        var deadLetterEntry = allEntries.Single(entry => entry.SourceGrainKey == composedKey);

        // The wall rides the row: recovered from the composed source key, not stored
        // as a separate per-message field.
        Assert.Equal(tenant, deadLetterEntry.Tenant);
        Assert.Equal(composedKey, deadLetterEntry.SourceGrainKey);
    }
}
