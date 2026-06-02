using Edict.Contracts.Sagas;
using Edict.Core.Sagas;

namespace Edict.Core.Tests.Saga;

public sealed class SagaTimeoutAttributeReaderTests
{
    [Fact]
    public void Read_ShouldReturnNone_WhenAttributeAbsent()
    {
        Assert.Equal(SagaTimeoutDeclaration.None, SagaTimeoutAttributeReader.Read(typeof(NoAttributeSaga)));
    }

    [Fact]
    public void Read_ShouldParseDuration_WhenAttributeDeclaresOne()
    {
        var declaration = SagaTimeoutAttributeReader.Read(typeof(FiniteCapSaga));

        Assert.Equal(SagaTimeoutDeclaration.ForDuration(TimeSpan.FromDays(1)), declaration);
    }

    [Fact]
    public void Read_ShouldReturnUnbounded_WhenAttributeOptsOut()
    {
        Assert.Equal(SagaTimeoutDeclaration.AsUnbounded, SagaTimeoutAttributeReader.Read(typeof(UnboundedSaga)));
    }

    sealed class NoAttributeSaga;

    [EdictSagaTimeout("1.00:00:00")]
    sealed class FiniteCapSaga;

    [EdictSagaTimeout(Unbounded = true)]
    sealed class UnboundedSaga;
}
