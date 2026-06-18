using Edict.Contracts.Commands;
using Edict.Contracts.Projections;
using Edict.Core.Projections;
using Edict.Tests.Conformance.Projections;
using Edict.Tests.Conformance.Reactivation;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// End-to-end read-your-writes conformance: a command's <c>Accepted</c> result
/// carries a cursor, and a cursor read after the projection has been forced to
/// reactivate answers <see cref="EdictReadStatus.CursorReached"/> with the row the
/// command produced. Forcing reactivation is the point of the scenario — it proves
/// the answer comes from the <em>persisted</em> correlation ring seeding the wait
/// mirror at activation, not merely the live in-memory mirror, so read-your-writes
/// survives a deactivate/reactivate against a real grain-state store.
/// <para>
/// Timeout, eviction, and any-applied semantics are pure in-grain logic with no
/// backend dependency, so they are unit-tested in <c>Edict.Core.Tests</c> against a
/// virtual clock rather than bound here.
/// </para>
/// </summary>
public abstract class ProjectionCursorReadScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected ProjectionCursorReadScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CursorRead_AfterReactivation_AnswersCursorReachedWithTheWrittenRow()
    {
        // Arrange — send a command and capture the cursor it echoes.
        var orderId = Guid.NewGuid();
        var result = await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-RYW"));
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(result).Cursor;

        // The row must have landed (and so the persisted correlation ring advanced)
        // before we force the reactivation.
        var store = _fixture.GetTableStore<OrderTableRow>("orderprojection");
        await TableProjectionWaiters.WaitForRowAsync(store, orderId.ToString(), orderId.ToString());

        // Force the projection to drop its in-memory state and come back from durable
        // storage, so a CursorReached answer can only come from the persisted ring
        // re-seeding the wait mirror at activation.
        var probe = _fixture.GrainFactory.GetGrain<IOrderListProjectionProbe>(orderId);
        await DeactivationWaiter.DeactivateAndConfirmAsync(probe);

        // Act — read through the grain with the cursor against the reactivated grain.
        var projection = _fixture.GrainFactory
            .GetGrain<IOrderListProjectionProbe>(orderId)
            .AsReference<IEdictProjectionBuilder>();
        var read = await projection.EdictReadRowAsync(orderId.ToString(), cursor.ConversationId, timeout: null);

        // Assert
        Assert.Equal(EdictReadStatus.CursorReached, read.Status);
        var row = Assert.IsType<OrderTableRow>(read.Payload);
        Assert.Equal(1, row.OrderCount);
    }
}
