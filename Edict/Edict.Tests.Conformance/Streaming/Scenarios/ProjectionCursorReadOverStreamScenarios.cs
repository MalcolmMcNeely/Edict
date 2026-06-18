using Edict.Contracts.Commands;
using Edict.Contracts.Projections;
using Edict.Core.Projections;
using Edict.Tests.Conformance.Projections;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis read-your-writes conformance: a command's <c>Accepted</c> result
/// carries a cursor, and once the event the command raised has been delivered over
/// the fixture's <strong>real</strong> stream provider and applied to the List
/// projection, a cursor read answers <see cref="EdictReadStatus.CursorReached"/>
/// with the row the command produced. This is the streaming half of the guarantee:
/// it proves a <em>real-stream delivery signals the cursor wait</em> over a single
/// hop (command → event over a real stream → List projection grain → cursor read).
/// It does not force reactivation and does not re-walk the saga bridge — those are
/// covered by the persistence axis and by
/// <c>SagaSendCommandEffectDeliversScenarios</c> / <c>ProjectionDeliveryScenarios</c>.
/// <para>
/// The neat axis split: streaming proves a real-stream delivery signals the cursor;
/// the persistence axis's <c>ProjectionCursorReadScenarios</c> proves the persisted
/// correlation ring survives a deactivate/reactivate.
/// </para>
/// <para>
/// Honest limit: this proves the <em>mechanism</em> — the cursor handshake over a
/// real stream — not the sample's <c>AutoOffsetReset</c> choice. Like every fixture
/// it runs <c>Earliest</c>, so it would not have caught the sample's <c>Latest</c>
/// startup-offset drop; guarding that configuration choice is the sample's own
/// concern, not a conformance property.
/// </para>
/// </summary>
public abstract class ProjectionCursorReadOverStreamScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected ProjectionCursorReadOverStreamScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CursorRead_AfterRealStreamDelivery_AnswersCursorReachedWithTheWrittenRow()
    {
        // Arrange — send a command and capture the cursor it echoes.
        var orderId = Guid.NewGuid();
        var result = await _fixture.Sender.SendAsync(new PlaceOrderCommand(orderId, "SKU-RYW-STREAM"));
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(result).Cursor;

        // Wait deterministically for the real-stream-delivered event to land as the
        // List-projection row before the cursor read, so the read is never racing
        // real-stream delivery latency. The waiter polls until true and only caps
        // out on genuine non-delivery.
        var store = _fixture.GetTableStore<OrderTableRow>("orderprojection");
        await TableProjectionWaiters.WaitForRowAsync(store, orderId.ToString(), orderId.ToString());

        // Act — read through the grain with the cursor. The timeout is incidental:
        // it covers only the sub-millisecond gap between the row write and the
        // in-memory mark signal, because delivery already happened.
        var projection = _fixture.GrainFactory
            .GetGrain<IOrderListProjectionProbe>(orderId)
            .AsReference<IEdictProjectionBuilder>();
        var read = await projection.EdictReadRowAsync(orderId.ToString(), cursor.ConversationId, TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(EdictReadStatus.CursorReached, read.Status);
        var row = Assert.IsType<OrderTableRow>(read.Payload);
        Assert.Equal(1, row.OrderCount);
    }
}
