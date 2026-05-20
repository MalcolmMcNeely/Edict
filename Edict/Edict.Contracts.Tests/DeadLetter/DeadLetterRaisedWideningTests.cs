using Edict.Contracts.DeadLetter;

namespace Edict.Contracts.Tests.DeadLetter;

public sealed class DeadLetterRaisedWideningTests
{
    [Fact]
    public void EdictDeadLetterRaised_ShouldDefaultFailureKindToEffectFailure_AndClaimCheckKeyToNull()
    {
        var raised = new EdictDeadLetterRaised();

        Assert.Equal(EdictDeadLetterFailureKind.EffectFailure, raised.FailureKind);
        Assert.Null(raised.ClaimCheckKey);
    }

    [Fact]
    public void EdictDeadLetterRaised_ShouldCarryClaimCheckKeyAndBlobMissingKind_WhenSet()
    {
        var raised = new EdictDeadLetterRaised
        {
            ClaimCheckKey = "blob/abcd",
            FailureKind = EdictDeadLetterFailureKind.BlobMissing,
        };

        Assert.Equal("blob/abcd", raised.ClaimCheckKey);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, raised.FailureKind);
    }

    [Fact]
    public void EdictDeadLetterEntry_ShouldDefaultFailureKindToEffectFailure_AndClaimCheckKeyToNull()
    {
        var entry = new EdictDeadLetterEntry();

        Assert.Equal(EdictDeadLetterFailureKind.EffectFailure, entry.FailureKind);
        Assert.Null(entry.ClaimCheckKey);
    }
}
