using Edict.Core.DeadLetter;
using Edict.Telemetry;

using Xunit;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Cross-substrate proof of the observable claim-check key contract against the
/// <c>Guid</c>-keyed <c>IEdictClaimCheckStore</c> seam. Every store is addressed
/// by the event's own <c>EventId</c>: a put-then-get by that id returns the
/// identical body, and a get for an id no store holds raises the typed
/// <c>EdictClaimCheckFetchException</c> that the dead-letter classifier maps to
/// the <c>Substrate</c> bucket. The scenarios assert external behavior only —
/// the bytes returned, the exception type, the classification — never how a
/// backend encodes the <c>EventId</c> as its key. There is no re-put scenario:
/// <c>PutAsync</c> runs once per event at the outbox enqueue boundary and is
/// never re-called on re-drain, so that path cannot be reached.
/// </summary>
public abstract class ClaimCheckKeyContractScenarios<TFixture>
    where TFixture : IClaimCheckStoreFixture
{
    readonly TFixture _fixture;

    protected ClaimCheckKeyContractScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PutThenGetByEventId_ShouldReturnIdenticalBytes()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        byte[] payload = [0x10, 0x20, 0x30, 0x40, 0x50];

        // Act
        await _fixture.PutClaimCheckAsync(eventId, payload, CancellationToken.None);
        var fetched = await _fixture.GetClaimCheckAsync(eventId, CancellationToken.None);

        // Assert
        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task GetUnknownEventId_ShouldThrowFetchException_ClassifyingToSubstrate()
    {
        // Arrange
        var unknownEventId = Guid.NewGuid();

        // Act
        var exception = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => _fixture.GetClaimCheckAsync(unknownEventId, CancellationToken.None));

        // Assert
        Assert.Equal(unknownEventId, exception.EventId);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            DeadLetterFailureClassifier.Classify(exception));
    }
}
