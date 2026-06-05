using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Commands;

// Drives a command through the real engine and reads the published event off the
// outbox seam, so Raise's OccurredAt stamp is asserted on the wire event a
// consumer would receive — never by calling HandleAsync directly.
[Collection(RaiseStampCollection.Name)]
public sealed class CommandHandlerRaiseTests(RaiseStampClusterFixture fixture)
{
    static readonly Guid CounterId = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Raise_ShouldStampOccurredAtFromTheClock_WhenHandlerRaisesAnEvent()
    {
        // Arrange
        RaiseStampCapturingExecutor.Reset();

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId));

        // Assert
        var published = Assert.Single(RaiseStampCapturingExecutor.Captured);
        Assert.Equal(RaiseStampClusterFixture.FrozenNow, published.OccurredAt);
    }
}
