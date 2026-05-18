using System.Reflection;

using Edict.Contracts.Commands;

using static VerifyXunit.Verifier;

namespace Edict.Contracts.Tests.Commands;

file sealed record SampleCommand(Guid AccountId) : EdictCommand;

public class CommandTests
{
    [Fact]
    public void Commands_with_the_same_state_are_equal()
    {
        var accountId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        var first = new SampleCommand(accountId) { CommandId = commandId };
        var second = new SampleCommand(accountId) { CommandId = commandId };

        Assert.Equal(first, second);
    }

    [Fact]
    public Task Command_base_exposes_only_a_settable_CommandId_and_no_trace_fields()
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
