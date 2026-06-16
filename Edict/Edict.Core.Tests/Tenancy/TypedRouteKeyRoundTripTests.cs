using MessagePack;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// A typed [EdictRouteKey] is widened from the bare Guid, so the carry now depends
// on the wrapping struct surviving a full MessagePack round-trip the way a stream
// hop deserializes it. This pins that directly for the typed key.
public sealed class TypedRouteKeyRoundTripTests
{
    [Fact]
    public void Command_ShouldPreserveTypedRouteKey_AcrossMessagePackRoundTrip()
    {
        // Arrange
        var employeeId = new EmployeeId(new Guid("33333333-3333-3333-3333-333333333333"));
        var command = new AddEmployeeCommand(employeeId, "Engineering");

        // Act
        var bytes = MessagePackSerializer.Serialize(command);
        var roundTripped = MessagePackSerializer.Deserialize<AddEmployeeCommand>(bytes);

        // Assert
        Assert.Equal(employeeId, roundTripped.EmployeeId);
        Assert.Equal("Engineering", roundTripped.Department);
    }
}
