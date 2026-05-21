using Edict.Core.Sagas;

namespace Edict.Core.Tests.Saga;

public sealed class EdictSagaTests
{
    [Fact]
    public void Dispatch_ShouldThrow_WhenCalledTwiceWithinOneEventHandler()
    {
        var buffer = new SagaDispatchBuffer();

        buffer.Set(new SagaTrackerCommand(Guid.NewGuid()));

        Assert.Throws<InvalidOperationException>(() => buffer.Set(new SagaTrackerCommand(Guid.NewGuid())));
    }

    [Fact]
    public void Set_ShouldSucceedAgain_WhenResetBetweenEvents()
    {
        var buffer = new SagaDispatchBuffer();

        buffer.Set(new SagaTrackerCommand(Guid.NewGuid()));
        buffer.Reset();
        buffer.Set(new SagaTrackerCommand(Guid.NewGuid()));

        Assert.NotNull(buffer.Take());
    }
}
