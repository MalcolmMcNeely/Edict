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
    public void EdictRouteKeyAttribute_ShouldMarkThePropertyItIsAppliedTo()
    {
        var marked = typeof(TransferFunds)
            .GetProperty(nameof(TransferFunds.AccountId))!
            .GetCustomAttribute<EdictRouteKeyAttribute>();

        Assert.NotNull(marked);
    }

    [Fact]
    public void EdictRouteKeyAttribute_ShouldOnlyBePlaceableOnProperties()
    {
        var usage = typeof(EdictRouteKeyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
    }
}
