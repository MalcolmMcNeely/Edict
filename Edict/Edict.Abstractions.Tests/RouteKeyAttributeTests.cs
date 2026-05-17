using System.Reflection;

using Edict.Abstractions;

namespace Edict.Abstractions.Tests;

file sealed record TransferFunds(Guid AccountId) : Command
{
    [RouteKey]
    public Guid AccountId { get; init; } = AccountId;
}

public class RouteKeyAttributeTests
{
    [Fact]
    public void RouteKey_marks_the_property_it_is_applied_to()
    {
        var marked = typeof(TransferFunds)
            .GetProperty(nameof(TransferFunds.AccountId))!
            .GetCustomAttribute<RouteKeyAttribute>();

        Assert.NotNull(marked);
    }

    [Fact]
    public void RouteKey_can_only_be_placed_on_properties()
    {
        var usage = typeof(RouteKeyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
    }
}
