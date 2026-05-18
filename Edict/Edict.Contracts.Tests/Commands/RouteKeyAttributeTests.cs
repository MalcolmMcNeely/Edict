using System.Reflection;

using Edict.Contracts.Commands;

namespace Edict.Contracts.Tests.Commands;

file sealed record TransferFunds(Guid AccountId) : EdictCommand
{
    [EdictRouteKey]
    public Guid AccountId { get; init; } = AccountId;
}

public class RouteKeyAttributeTests
{
    [Fact]
    public void RouteKey_marks_the_property_it_is_applied_to()
    {
        var marked = typeof(TransferFunds)
            .GetProperty(nameof(TransferFunds.AccountId))!
            .GetCustomAttribute<EdictRouteKeyAttribute>();

        Assert.NotNull(marked);
    }

    [Fact]
    public void RouteKey_can_only_be_placed_on_properties()
    {
        var usage = typeof(EdictRouteKeyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
    }
}
