using Edict.Core.DeadLetter;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.ClaimCheck;

// The observable claim-check key contract against the Guid-keyed
// IEdictClaimCheckStore seam: every store is addressed by the event's own
// EventId, a put-then-get by that id returns the identical body, and a get for
// an id no store holds raises the typed EdictClaimCheckFetchException that the
// dead-letter classifier maps to the Substrate bucket. Provider-invariant — the
// contract the production stores conform to is proven once in-memory.
public sealed class InMemoryClaimCheckKeyContractTests
{
    [Fact]
    public async Task PutThenGetByEventId_ShouldReturnIdenticalBytes()
    {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var eventId = Guid.NewGuid();
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];

        // Act
        await store.PutAsync(eventId, payload, CancellationToken.None);
        var fetched = await store.GetAsync(eventId, CancellationToken.None);

        // Assert
        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task GetUnknownEventId_ShouldThrowFetchException_ClassifyingToSubstrate()
    {
        // Arrange
        var store = new InMemoryClaimCheckStore();
        var unknownEventId = Guid.NewGuid();

        // Act
        var exception = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => store.GetAsync(unknownEventId, CancellationToken.None));

        // Assert
        Assert.Equal(unknownEventId, exception.EventId);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            DeadLetterFailureClassifier.Classify(exception));
    }
}
