using Edict.Contracts.Tenancy;
using Edict.Core.DeadLetter;
using Edict.Telemetry;

using Xunit;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Cross-substrate proof of the observable claim-check key contract against the
/// tenant-folded <c>IEdictClaimCheckStore</c> seam. Every store is addressed by
/// the event's own <c>EventId</c> folded behind the tenant wall: a put-then-get
/// by that id returns the identical body, a get for an id no store holds raises
/// the typed <c>EdictClaimCheckFetchException</c> that the dead-letter classifier
/// maps to the <c>Substrate</c> bucket, and a body parked under one tenant is
/// unreachable from another tenant's wall even at the same <c>EventId</c>. The
/// scenarios assert external behavior only — the bytes returned, the exception
/// type, the classification, the isolation — never how a backend encodes the key.
/// There is no re-put scenario: <c>PutAsync</c> runs once per event at the outbox
/// enqueue boundary and is never re-called on re-drain, so that path cannot be reached.
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
        await _fixture.PutClaimCheckAsync(tenant: null, eventId, payload, CancellationToken.None);
        var fetched = await _fixture.GetClaimCheckAsync(tenant: null, eventId, CancellationToken.None);

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
            () => _fixture.GetClaimCheckAsync(tenant: null, unknownEventId, CancellationToken.None));

        // Assert
        Assert.Equal(unknownEventId, exception.EventId);
        Assert.Equal(
            SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate,
            DeadLetterFailureClassifier.Classify(exception));
    }

    [Fact]
    public async Task PutThenGetUnderSameTenant_ShouldReturnIdenticalBytes()
    {
        // Arrange
        var tenant = EdictTenantId.Of("acme");
        var eventId = Guid.NewGuid();
        byte[] payload = [0xA1, 0xB2, 0xC3, 0xD4];

        // Act
        await _fixture.PutClaimCheckAsync(tenant, eventId, payload, CancellationToken.None);
        var fetched = await _fixture.GetClaimCheckAsync(tenant, eventId, CancellationToken.None);

        // Assert
        Assert.Equal(payload, fetched.ToArray());
    }

    [Fact]
    public async Task GetUnderAnotherTenant_ShouldThrowFetchException_WhenBodyParkedUnderOneTenant()
    {
        // Arrange
        var owningTenant = EdictTenantId.Of("acme");
        var intruderTenant = EdictTenantId.Of("globex");
        var eventId = Guid.NewGuid();
        byte[] payload = [0xA1, 0xB2, 0xC3, 0xD4];
        await _fixture.PutClaimCheckAsync(owningTenant, eventId, payload, CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => _fixture.GetClaimCheckAsync(intruderTenant, eventId, CancellationToken.None));

        // Assert: the largest, most sensitive spilled body never leaks across the wall.
        Assert.Equal(eventId, exception.EventId);
    }
}
