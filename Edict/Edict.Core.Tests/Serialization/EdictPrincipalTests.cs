using Edict.Contracts.Audit;

using MessagePack;

using Xunit;

namespace Edict.Core.Tests.Serialization;

public sealed class EdictPrincipalTests
{
    [Fact]
    public void Of_ShouldRoundTripThroughMessagePack_AsABareString()
    {
        // Arrange — an arbitrary IdP slug Edict imposes no format on.
        var principal = EdictPrincipal.Of("auth0|abc-123");

        // Act
        var bytes = MessagePackSerializer.Serialize(principal);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        var roundTripped = MessagePackSerializer.Deserialize<EdictPrincipal>(bytes);

        // Assert
        Assert.Equal("{\"Value\":\"auth0|abc-123\"}", json);
        Assert.Equal(principal, roundTripped);
    }
}
