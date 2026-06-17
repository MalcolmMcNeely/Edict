using Edict.Contracts.DeadLetter;
using Edict.Contracts.Projections;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;
using Edict.Core.Tenancy;
using Edict.Telemetry;
using Edict.Tests.Conformance.ClaimCheck;
using Edict.Tests.Conformance.Tenancy;

using Orleans;
using Orleans.Runtime;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The adversarial battery: not the happy-path "another tenant's identical query is
/// empty" denial, but isolation held <em>under attack</em>. A thief holds tenant
/// <c>acme</c>'s raw route key — the bearer capability the whole design exists to
/// demote — and wields it against every entry point a stolen key could travel:
/// command send, projection read, claim-check fetch, and dead-letter. Each door
/// denies on a real persistence backend, so a regression that re-promoted the key to
/// a capability would turn one of these red rather than ship a cross-wall leak. The
/// doors deny by different mechanisms, which is the point: the command grain is
/// guarded by the runtime isolation call filter, the read by the reader's shape (no
/// partition key to forge), the claim-check by the tenant fold in the store key, and
/// the dead-letter by being operator-scoped with no tenant-addressable partition at
/// all. A stolen key is a useful lever at none of them.
/// </summary>
public abstract class StolenRouteKeyScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IEdictTenancyConformanceFixture, IClaimCheckStoreFixture
{
    static readonly EdictTenantId Owner = EdictTenantId.Of("acme");
    static readonly EdictTenantId Thief = EdictTenantId.Of("globex");

    readonly TFixture _fixture;

    protected StolenRouteKeyScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StolenKey_AtCommandSend_IsDeniedByTheIsolationCallFilter()
    {
        RequestContext.Clear();
        var employeeId = new EmployeeId(Guid.NewGuid());
        var stolenKey = EdictKeyComposer.Compose(Owner, employeeId.Value.ToString("N"));

        // Arrange + Act: the thief reaches acme's command grain directly with the
        // stolen composed key, from no established wall and without authorizing the
        // crossing — the raw path the sender's composition chokepoint would never
        // produce. The runtime isolation call filter compares the grain's own key
        // tenant to the caller's ambient tenant and denies the divergence.
        var grain = _fixture.GrainFactory.GetGrain<IEmployeeOutboxProbe>(stolenKey);
        var denial = await Assert.ThrowsAsync<EdictCrossTenantAccessException>(
            () => grain.GetPendingOutboxCountAsync());

        // Assert: the call was denied, and the denial reached the client as itself —
        // the filter's typed exception survives the silo-to-client serialization hop
        // the stolen key travels, rather than surfacing as an opaque codec failure.
        Assert.Contains("denied", denial.Message, StringComparison.OrdinalIgnoreCase);

        // The operator path is the one sanctioned crossing: with it authorized, the
        // identical reach proceeds, proving the filter — not a broken grain — is the
        // gate that denied the unauthorized attempt.
        TenantCrossing.Authorize();
        var pending = await grain.GetPendingOutboxCountAsync();
        Assert.Equal(0, pending);
        RequestContext.Clear();
    }

    [Fact]
    public async Task StolenKey_AtProjectionRead_GivesTheThiefNoLever()
    {
        var reader = _fixture.EmployeeDirectoryReader;

        // Arrange: acme writes three employees under its own wall. The thief, by the
        // adversary's premise, learns every one of these raw EmployeeId keys.
        ConformanceTenantSource.Current = Owner;
        for (var index = 0; index < 3; index++)
        {
#pragma warning disable EDICT024
            await _fixture.Sender.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Engineering"));
#pragma warning restore EDICT024
        }
        var ownerDirectory = await WaitForPartitionCountAsync(reader, expectedCount: 3);

        // Act: the thief reads from its own wall. Possessing acme's raw keys buys
        // nothing — the reader takes no partition key, so there is no seam to feed a
        // stolen key into and no way to express acme's partition.
        ConformanceTenantSource.Current = Thief;
        var thiefDirectory = await reader.QueryMyPartitionAsync();

        // Assert: acme sees its three; the thief sees none, by construction.
        Assert.Equal(3, ownerDirectory.Rows.Count);
        Assert.Empty(thiefDirectory.Rows);
    }

    [Fact]
    public async Task StolenKey_AtClaimCheckFetch_IsUnreachableAcrossTheWall()
    {
        var eventId = Guid.NewGuid();
        byte[] payload = [0xA1, 0xB2, 0xC3, 0xD4, 0xE5];

        // Arrange: acme parks a spilled body — the largest, most sensitive payloads
        // take this branch — under its own wall.
        await _fixture.PutClaimCheckAsync(Owner, eventId, payload, CancellationToken.None);

        // Act + Assert: the thief fetches at the very same EventId from its own wall.
        // The store folds the tenant into its backing key, so acme's body is not
        // there to return; the fetch raises the typed miss rather than leaking it.
        var denial = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => _fixture.GetClaimCheckAsync(Thief, eventId, CancellationToken.None));
        Assert.Equal(eventId, denial.EventId);
    }

    [Fact]
    public async Task StolenKey_AtDeadLetter_StaysOperatorScopedWithNoTenantPartition()
    {
        RequestContext.Clear();
        var employeeId = new EmployeeId(Guid.NewGuid());
        var composedKey = EdictKeyComposer.Compose(Owner, employeeId.Value.ToString("N"));

        // The drain probe reaches a tenant-keyed command grain directly, so authorize
        // it as the operator-driven crossing it is rather than a stolen-key reach.
        TenantCrossing.Authorize();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.ShouldFail = true;

        // Arrange: poison acme's tenant-scoped command until its outbox entry promotes.
        ConformanceTenantSource.Current = Owner;
#pragma warning disable EDICT024
        await _fixture.Sender.SendAsync(new AddEmployeeCommand(employeeId, "Engineering"));
#pragma warning restore EDICT024

        var probe = _fixture.GrainFactory.GetGrain<IEmployeeOutboxProbe>(composedKey);
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            await probe.ForceDrainViaReminderAsync();
            return _fixture.OutboxFault.FailedAttempts >= 2;
        });

        // Heal so the promoted EdictDeadLetterRaised entry can publish instead of
        // looping on the same fail/promote cycle.
        _fixture.OutboxFault.ShouldFail = false;
        await WaitUntilAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            await probe.ForceDrainViaReminderAsync();
            return await probe.GetPendingOutboxCountAsync() == 0;
        });

        var deadLetterTable = _fixture.GetTableStore<EdictDeadLetterEntry>(EdictDeadLetterTable.Name);
        await WaitUntilAsync(async () =>
        {
            var entries = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
            return entries.Any(entry => entry.SourceGrainKey == composedKey);
        });

        // Act: the failure lands in the single operator-scoped partition, tagged with
        // acme's wall so an operator can filter by tenant.
        var operatorRows = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        var deadLetterEntry = operatorRows.Single(entry => entry.SourceGrainKey == composedKey);

        // The thief, knowing acme's wall, addresses a partition named by a tenant —
        // the natural stolen-key reach. There is none: dead-letter is never
        // partitioned by tenant, so the forensic payload has no tenant-addressable home.
        var thiefPartition = await deadLetterTable.QueryPartitionAsync(Thief.Value);

        // Assert: the row is operator-scoped and tenant-tagged; no tenant partition
        // exposes it.
        Assert.Equal(Owner, deadLetterEntry.Tenant);
        Assert.DoesNotContain(thiefPartition, entry => entry.SourceGrainKey == composedKey);
        RequestContext.Clear();
    }

    static async Task<EdictProjectionPartitionRead<EmployeeDirectoryRow>> WaitForPartitionCountAsync(
        IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> reader, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        EdictProjectionPartitionRead<EmployeeDirectoryRow> read;
        do
        {
            read = await reader.QueryMyPartitionAsync();
            if (read.Rows.Count >= expectedCount)
            {
                return read;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        while (DateTimeOffset.UtcNow < deadline);

        return read;
    }

    static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
    }
}
