using Edict.Core.Audit;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Audit;

// Fail-closed at the origin: auditing on and the resolver yields nobody, so the
// send is refused at the edge with a typed throw a caller observes, before any
// command is dispatched or event persisted. Recording an action with no actor is
// the breach auditing exists to prevent.
[Collection(AuditOriginCollection.Name)]
public sealed class PrincipalFailClosedTests(AuditOriginClusterFixture fixture)
{
    static readonly Guid CounterId = new("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public async Task SendAsync_ShouldThrowSynchronously_WhenAuditingOnAndResolverYieldsNoPrincipal()
    {
        // Arrange
        AuditCapturingExecutor.Reset();
        AuditPrincipalSource.Current = null;

        // Act + Assert
        await Assert.ThrowsAsync<EdictMissingPrincipalException>(
            () => fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId)));

        // The throw landed before dispatch, so nothing reached the outbox.
        Assert.Empty(AuditCapturingExecutor.Captured);
    }
}
