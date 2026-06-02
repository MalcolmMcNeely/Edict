using Edict.Core.Sagas;

namespace Edict.Core.Tests.Saga;

public sealed class SagaDeadlineResolverTests
{
    static readonly DateTimeOffset StartedAt = new(2026, 6, 2, 9, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan FiniteDefault = TimeSpan.FromDays(7);

    [Fact]
    public void Resolve_ShouldUseExplicitDuration_WhenAttributeDeclaresOne()
    {
        var deadline = SagaDeadlineResolver.Resolve(
            SagaTimeoutDeclaration.ForDuration(TimeSpan.FromHours(24)), FiniteDefault, StartedAt);

        Assert.Equal(StartedAt.AddHours(24), deadline);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenAttributeIsUnbounded_EvenWithFiniteDefault()
    {
        var deadline = SagaDeadlineResolver.Resolve(
            SagaTimeoutDeclaration.AsUnbounded, FiniteDefault, StartedAt);

        Assert.Null(deadline);
    }

    [Fact]
    public void Resolve_ShouldFallBackToDefault_WhenAttributeAbsent()
    {
        var deadline = SagaDeadlineResolver.Resolve(
            SagaTimeoutDeclaration.None, FiniteDefault, StartedAt);

        Assert.Equal(StartedAt + FiniteDefault, deadline);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenAttributeAbsentAndDefaultIsNull()
    {
        var deadline = SagaDeadlineResolver.Resolve(
            SagaTimeoutDeclaration.None, defaultTimeout: null, StartedAt);

        Assert.Null(deadline);
    }
}
