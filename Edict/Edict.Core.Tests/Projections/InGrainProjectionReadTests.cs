using Edict.Contracts.Commands;
using Edict.Contracts.Projections;
using Edict.Core.Projections;
using Edict.Core.Tests.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edict.Core.Tests.Projections;

// End-to-end read-your-writes for the in-grain (State) projection species: a
// command's Accepted cursor read after the inline payload write answers
// CursorReached (this is what the OutboxHost no-staged-effect onDrained fix
// makes possible — without it the in-grain commit never signals the cursor
// coordinator and the read blocks to a timeout). A cursorless read answers
// Immediate. Reading an in-grain projection through the List reader hits a read
// the species does not serve and surfaces EdictUnsupportedProjectionReadException.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class InGrainProjectionReadTests(SubstrateIndependentClusterFixture fixture)
{
    IEdictProjectionReader<TallyProjection> StateReader =>
        fixture.Cluster.Client.ServiceProvider.GetRequiredService<IEdictProjectionReader<TallyProjection>>();

    [Fact]
    public async Task ReadAsync_WithAcceptedCursor_AnswersCursorReachedWithThePersistedPayload()
    {
        // Arrange
        var tallyId = Guid.NewGuid();

        // Act
        var result = await fixture.Sender.SendAsync(new RecordTallyCommand(tallyId, 7));
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(result).Cursor;
        var read = await StateReader.ReadAsync(tallyId, after: cursor);

        // Assert
        Assert.Equal(EdictReadStatus.CursorReached, read.Status);
        Assert.Equal(7, read.Value!.Total);
    }

    [Fact]
    public async Task ReadAsync_WithNoCursor_AnswersImmediate()
    {
        // Arrange
        var tallyId = Guid.NewGuid();
        var result = await fixture.Sender.SendAsync(new RecordTallyCommand(tallyId, 3));
        var cursor = Assert.IsType<EdictCommandResult.Accepted>(result).Cursor;
        // Land the inline write first so the cursorless poll sees the payload.
        await StateReader.ReadAsync(tallyId, after: cursor);

        // Act
        var read = await StateReader.ReadAsync(tallyId);

        // Assert
        Assert.Equal(EdictReadStatus.Immediate, read.Status);
        Assert.Equal(3, read.Value!.Total);
    }

    [Fact]
    public async Task ListReader_AgainstInGrainProjection_ThrowsUnsupportedRead()
    {
        // Arrange
        var tallyId = Guid.NewGuid();
        await fixture.Sender.SendAsync(new RecordTallyCommand(tallyId, 1));
        var listReader = fixture.Cluster.Client.ServiceProvider
            .GetRequiredService<IEdictListProjectionReader<TallyProjection>>();

        // Act + Assert — the type routes to the in-grain grain, but a row read is
        // a read the State species does not serve.
        await Assert.ThrowsAsync<EdictUnsupportedProjectionReadException>(
            () => listReader.GetAsync(tallyId.ToString(), "row"));
    }
}
