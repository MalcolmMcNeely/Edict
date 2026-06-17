using Edict.Contracts.Commands;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;

using Xunit;

namespace Edict.Core.Tests.Tenancy;

// Grain state is stored under the grain's primary key, and that key is the
// composed route key. For a [EdictTenantScoped] aggregate the generator folds the
// tenant into the key (the emission the coverage guard pins), so two tenants at
// the same route key address two distinct grains — and therefore two distinct
// state slots that can never share a row. This pins the isolation that placement
// rests on, against the real tenant-scoped command type.
public sealed class TenantScopedGrainStateIsolationTests
{
    // The selector the generator emits for a tenant-scoped route-key type: the
    // static fold reads command.Tenant rather than null.
    static readonly Func<EdictCommand, string> TenantScopedSelector =
        command => EdictKeyComposer.Compose(command.Tenant, ((AddEmployeeCommand)command).EmployeeId.Value.ToString("N"));

    [Fact]
    public void TenantScopedCommand_ShouldComposeDistinctGrainKeysPerTenant_SoGrainStateIsIsolated()
    {
        // Arrange: the same EmployeeId under two different walls.
        var employeeId = new EmployeeId(Guid.NewGuid());

        // Act
        var acmeKey = TenantScopedSelector(
            new AddEmployeeCommand(employeeId, "Engineering") { Tenant = EdictTenantId.Of("acme") });
        var globexKey = TenantScopedSelector(
            new AddEmployeeCommand(employeeId, "Engineering") { Tenant = EdictTenantId.Of("globex") });

        // Assert: each grain (hence each state slot) is folded behind its own wall.
        Assert.Equal($"acme|{employeeId.Value:N}", acmeKey);
        Assert.Equal($"globex|{employeeId.Value:N}", globexKey);
        Assert.NotEqual(acmeKey, globexKey);
    }
}
