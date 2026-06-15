using Edict.Contracts.Audit;
using Edict.Core.Tests.Grains;

using Xunit;

namespace Edict.Core.Tests.Audit;

// Drives a command through the real client sender + engine under auditing, then
// reads the principal off the event raised on the command turn — the actor a
// consumer would see on the wire, never an internal field.
[Collection(AuditOriginCollection.Name)]
public sealed class PrincipalOriginStampingTests(AuditOriginClusterFixture fixture)
{
    static readonly Guid CounterId = new("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public async Task SendAsync_ShouldStampResolvedPrincipal_OntoTheRaisedEvent()
    {
        // Arrange
        AuditCapturingExecutor.Reset();
        AuditPrincipalSource.Current = EdictPrincipal.Of("alice");

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId));

        // Assert
        var published = Assert.Single(AuditCapturingExecutor.Captured);
        Assert.Equal(EdictPrincipal.Of("alice"), published.Principal);
    }

    [Fact]
    public async Task SendAsync_WithExplicitPrincipal_ShouldStampThatPrincipal_BypassingTheResolver()
    {
        // Arrange — the resolver would supply nobody; the explicit principal wins.
        AuditCapturingExecutor.Reset();
        AuditPrincipalSource.Current = null;

        // Act
        await fixture.Sender.SendAsync(new IncrementCounterCommand(CounterId), EdictPrincipal.Of("system-import"));

        // Assert
        var published = Assert.Single(AuditCapturingExecutor.Captured);
        Assert.Equal(EdictPrincipal.Of("system-import"), published.Principal);
    }
}
