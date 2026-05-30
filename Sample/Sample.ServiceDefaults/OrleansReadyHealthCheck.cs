using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sample.ServiceDefaults;

public sealed class OrleansReadyHealthCheck : IHealthCheck
{
    readonly OrleansReadyGate gate;

    public OrleansReadyHealthCheck(OrleansReadyGate gate) => this.gate = gate;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(gate.IsReady
            ? HealthCheckResult.Healthy("Orleans silo is Active.")
            : HealthCheckResult.Unhealthy("Orleans silo has not yet reached the Active lifecycle stage."));
}
