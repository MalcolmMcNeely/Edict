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
/// command send, projection read, claim-check fetch, and dead-letter. Each door denies
/// on a real persistence backend, so a regression that re-promoted the key to a
/// capability would turn one of these red rather than ship a cross-wall leak.
/// <para>
/// The four doors split into two kinds. Two are <em>typed-denial</em> doors: the command
/// send is refused by the runtime isolation call filter, and the claim-check fetch by the
/// tenant fold in the store key — each raises its own <c>Edict*</c> exception the thief
/// cannot talk past. The other two are <em>structural-isolation</em> proofs that feed the
/// stolen key through the one real input seam and watch the ambient fold defeat it: the
/// projection read takes the stolen owner key as a row key and folds the thief's own wall
/// over it, producing a doubly-walled key no composer could have made, which the reader's
/// chokepoint rejects as malformed unless it ever honoured an already-composed key; the
/// dead-letter row is operator-scoped and discriminated only by its tenant tag, so
/// filtering the one operator partition by acme's wall selects the row and by the thief's
/// excludes it. A stolen key is a useful lever at none of them.
/// </para>
/// </summary>
public abstract class StolenRouteKeyScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IEdictTenancyConformanceFixture, IClaimCheckStoreFixture
{
    // Unique per test instance: the directory table is one partition-per-tenant table
    // shared across every fixture in the assembly's Azurite, so a literal "acme" would
    // collide with the sibling list-read scenario's identical-tenant writes and overshoot
    // the count. A fresh pair per run keeps each scenario's wall in its own partition.
    readonly EdictTenantId Owner = EdictTenantId.Of("acme-" + Guid.NewGuid().ToString("N"));
    readonly EdictTenantId Thief = EdictTenantId.Of("globex-" + Guid.NewGuid().ToString("N"));

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
        // adversary's premise, learns acme's raw EmployeeId keys — and the fully-composed
        // owner key each one folds into.
        ConformanceTenantSource.Current = Owner;
        var stolenEmployee = new EmployeeId(Guid.NewGuid());
        for (var index = 0; index < 3; index++)
        {
            var employeeId = index == 0 ? stolenEmployee : new EmployeeId(Guid.NewGuid());
#pragma warning disable EDICT024
            await _fixture.Sender.SendAsync(new AddEmployeeCommand(employeeId, "Engineering"));
#pragma warning restore EDICT024
        }
        var ownerDirectory = await WaitForPartitionCountAsync(reader, expectedCount: 3);

        // acme point-gets its own row through the same seam, proving the row exists and
        // is reachable from acme's wall — so the thief's denial below is the stolen key
        // being rejected, not the row being absent.
        var ownerRow = await reader.GetMyAsync(stolenEmployee.Value.ToString("N"));

        // Act + Assert: the thief feeds the stolen, fully-composed owner key into
        // GetMyAsync — the reader's one input-taking seam. The reader folds the thief's
        // own wall over it, producing a doubly-walled key no composer could have made, and
        // rejects it at the chokepoint with the typed fault. The row would surface only if
        // the reader honoured the key as already-composed instead of folding its own wall.
        ConformanceTenantSource.Current = Thief;
        var stolenComposedKey = EdictKeyComposer.Compose(Owner, stolenEmployee.Value.ToString("N"));
        await Assert.ThrowsAsync<EdictMalformedRoutedKeyException>(() => reader.GetMyAsync(stolenComposedKey));

        // Assert: acme sees its three and reads its own row.
        Assert.Equal(3, ownerDirectory.Rows.Count);
        Assert.NotNull(ownerRow.Value);
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
        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            await probe.ForceDrainViaReminderAsync();
            return _fixture.OutboxFault.FailedAttempts >= 2;
        });

        // Heal so the promoted EdictDeadLetterRaised entry can publish instead of
        // looping on the same fail/promote cycle.
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

        // Act: the failure lands in the single operator-scoped partition, tagged with
        // acme's wall. Dead-letter is never partitioned by tenant, so the tenant tag is
        // the only seam that discriminates owner from thief — an operator filters the one
        // partition by it.
        var operatorRows = await deadLetterTable.QueryPartitionAsync(EdictDeadLetterTable.Name);
        var deadLetterEntry = operatorRows.Single(entry => entry.SourceGrainKey == composedKey);
        var underOwnersWall = operatorRows.Where(entry => entry.Tenant == Owner).ToList();
        var underThiefsWall = operatorRows.Where(entry => entry.Tenant == Thief).ToList();

        // Assert: the row is acme's by tag; filtering the operator partition on acme's
        // wall selects it and on the thief's wall excludes it — a positive proof the tag
        // discriminates, not reliance on a vacuously-empty tenant partition.
        Assert.Equal(Owner, deadLetterEntry.Tenant);
        Assert.Contains(underOwnersWall, entry => entry.SourceGrainKey == composedKey);
        Assert.DoesNotContain(underThiefsWall, entry => entry.SourceGrainKey == composedKey);
        RequestContext.Clear();
    }

    static async Task<EdictProjectionPartitionRead<EmployeeDirectoryRow>> WaitForPartitionCountAsync(
        IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> reader, int expectedCount)
    {
        EdictProjectionPartitionRead<EmployeeDirectoryRow> read = default;
        await ConformanceWaiters.WaitUntilAsync(async () =>
        {
            read = await reader.QueryMyPartitionAsync();
            return read.Rows.Count >= expectedCount;
        });
        return read;
    }
}
