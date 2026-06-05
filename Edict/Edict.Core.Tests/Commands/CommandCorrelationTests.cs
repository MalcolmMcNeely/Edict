using Edict.Contracts.Commands;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Commands;

// Drives commands through the real sender + engine and reads the correlation off
// the wire — the echoed cursor on Accepted and the raised event captured at the
// outbox seam — so each propagation hop is asserted on what a consumer would see,
// never by calling internals directly.
[Collection(RaiseStampCollection.Name)]
public sealed class CommandCorrelationTests(RaiseStampClusterFixture fixture)
{
    static readonly Guid CounterId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task SendAsync_ShouldMintCorrelationAndEchoOnCursor_WhenCommandCarriesNone()
    {
        // Act
        var result = await fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId));

        // Assert
        var accepted = Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.NotEqual(Guid.Empty, accepted.Cursor.CorrelationId);
    }

    [Fact]
    public async Task SendAsync_ShouldHonourCallerCorrelationOnCursor_WhenCommandCarriesOne()
    {
        // Arrange
        var callerCorrelation = Guid.NewGuid();

        // Act
        var result = await fixture.Sender.SendAsync(
            new IncrementCounterCommand(CounterId) { CorrelationId = callerCorrelation });

        // Assert
        var accepted = Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.Equal(callerCorrelation, accepted.Cursor.CorrelationId);
    }

    [Fact]
    public async Task Raise_ShouldStampHandlingCommandCorrelation_OntoTheRaisedEvent()
    {
        // Arrange
        RaiseStampCapturingExecutor.Reset();
        var callerCorrelation = Guid.NewGuid();

        // Act
        await fixture.Sender.SendAsync(
            new IncrementCounterCommand(CounterId) { CorrelationId = callerCorrelation });

        // Assert
        var published = Assert.Single(RaiseStampCapturingExecutor.Captured);
        Assert.Equal(callerCorrelation, published.CorrelationId);
    }
}
