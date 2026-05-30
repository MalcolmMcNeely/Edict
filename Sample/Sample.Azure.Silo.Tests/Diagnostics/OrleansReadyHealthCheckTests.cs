using Microsoft.Extensions.Diagnostics.HealthChecks;

using Orleans.Runtime;

using Sample.ServiceDefaults;

using Xunit;

namespace Sample.Azure.Silo.Tests.Diagnostics;

public sealed class OrleansReadyHealthCheckTests
{
    [Fact]
    public async Task Reports_unhealthy_before_active_stage_and_healthy_after()
    {
        // Arrange
        var gate = new OrleansReadyGate();
        var healthCheck = new OrleansReadyHealthCheck(gate);
        var lifecycle = new FakeSiloLifecycle();
        gate.Participate(lifecycle);
        var context = new HealthCheckContext();

        // Act
        var beforeActive = await healthCheck.CheckHealthAsync(context);

        await lifecycle.AdvanceToAsync(ServiceLifecycleStage.Active);

        var afterActive = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, beforeActive.Status);
        Assert.Equal(HealthStatus.Healthy, afterActive.Status);
    }

    sealed class FakeSiloLifecycle : ISiloLifecycle
    {
        readonly List<(int Stage, ILifecycleObserver Observer)> subscriptions = new();

        public int HighestCompletedStage { get; private set; }

        public int LowestStoppedStage { get; private set; } = int.MaxValue;

        public IDisposable Subscribe(string observerName, int stage, ILifecycleObserver observer)
        {
            subscriptions.Add((stage, observer));
            return new EmptyDisposable();
        }

        public async Task AdvanceToAsync(int targetStage)
        {
            foreach (var (stage, observer) in subscriptions.OrderBy(subscription => subscription.Stage))
            {
                if (stage <= targetStage)
                {
                    await observer.OnStart(default);
                    HighestCompletedStage = stage;
                }
            }
        }

        sealed class EmptyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
