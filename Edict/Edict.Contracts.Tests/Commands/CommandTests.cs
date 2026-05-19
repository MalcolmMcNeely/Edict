using System.Reflection;

using Edict.Contracts.Commands;

using static VerifyXunit.Verifier;

namespace Edict.Contracts.Tests.Commands;

file sealed record SampleCommand(Guid AccountId) : EdictCommand;

public class CommandTests
{
    [Fact]
    public void EdictCommand_ShouldBeEqual_WhenSameState()
    {
        var accountId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        var first = new SampleCommand(accountId) { CommandId = commandId };
        var second = new SampleCommand(accountId) { CommandId = commandId };

        Assert.Equal(first, second);
    }

    [Fact]
    public Task EdictCommand_ShouldExposeOnlySettableCommandIdAndNoTraceFields()
    {
        var properties = typeof(EdictCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => new
            {
                property.Name,
                Type = property.PropertyType.Name,
                CanRead = property.CanRead,
                HasInitOnlySetter = property.SetMethod is { } setter
                    && setter.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Any(modifier => modifier.Name == "IsExternalInit"),
            });

        return Verify(properties);
    }
}
