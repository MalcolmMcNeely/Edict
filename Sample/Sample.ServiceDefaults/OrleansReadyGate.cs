using Orleans.Runtime;

namespace Sample.ServiceDefaults;

public sealed class OrleansReadyGate : ILifecycleParticipant<ISiloLifecycle>
{
    public bool IsReady { get; private set; }

    public void Participate(ISiloLifecycle lifecycle) =>
        lifecycle.Subscribe(nameof(OrleansReadyGate), ServiceLifecycleStage.Active, _ =>
        {
            IsReady = true;
            return Task.CompletedTask;
        });
}
