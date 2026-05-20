using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;

namespace Edict.Core.Tests.ClaimCheck;

public sealed class EnvelopeCodecTests
{
    [Fact]
    public void WrapInline_ShouldReturnEnvelopeCarryingTheInlineBranch()
    {
        byte[] payload = [1, 2, 3];

        var envelope = EnvelopeCodec.WrapInline(payload);

        Assert.Same(payload, envelope.InlinePayload);
        Assert.Null(envelope.ClaimCheckKey);
    }

    [Fact]
    public void WrapInline_ShouldThrow_WhenBytesAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => EnvelopeCodec.WrapInline(null!));
    }

    [Fact]
    public void WrapPointer_ShouldReturnEnvelopeCarryingThePointerBranch()
    {
        var envelope = EnvelopeCodec.WrapPointer("blob-key-1");

        Assert.Equal("blob-key-1", envelope.ClaimCheckKey);
        Assert.Null(envelope.InlinePayload);
    }

    [Fact]
    public void WrapPointer_ShouldThrow_WhenKeyIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => EnvelopeCodec.WrapPointer(null!));
    }

    [Fact]
    public void WrapPointer_ShouldThrow_WhenKeyIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => EnvelopeCodec.WrapPointer(string.Empty));
    }

    [Fact]
    public void IsEnvelope_ShouldReturnTrue_WhenEventIsEdictEventEnvelope()
    {
        EdictEvent envelope = EnvelopeCodec.WrapInline([0xFF]);

        Assert.True(EnvelopeCodec.IsEnvelope(envelope));
    }

    [Fact]
    public void IsEnvelope_ShouldReturnFalse_WhenEventIsConcreteDomainEvent()
    {
        EdictEvent domain = new OrderPlacedEvent(Guid.NewGuid(), "SKU-1");

        Assert.False(EnvelopeCodec.IsEnvelope(domain));
    }

    [Fact]
    public void TryGetInline_ShouldReturnPayload_WhenInlineBranch()
    {
        byte[] payload = [0x0A, 0x0B];
        var envelope = EnvelopeCodec.WrapInline(payload);

        Assert.Same(payload, EnvelopeCodec.TryGetInline(envelope));
    }

    [Fact]
    public void TryGetPointer_ShouldReturnKey_WhenPointerBranch()
    {
        var envelope = EnvelopeCodec.WrapPointer("blob/123");

        Assert.Equal("blob/123", EnvelopeCodec.TryGetPointer(envelope));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenBothBranchesAreSet()
    {
        Assert.Throws<ArgumentException>(() => new EdictEventEnvelope([1], "blob"));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNeitherBranchIsSet()
    {
        Assert.Throws<ArgumentException>(() => new EdictEventEnvelope(null, null));
    }
}
