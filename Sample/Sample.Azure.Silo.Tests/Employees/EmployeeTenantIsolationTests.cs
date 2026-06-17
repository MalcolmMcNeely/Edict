using Edict.Contracts.Tenancy;
using Edict.Testing;

using Sample.Contracts.Employees.Commands;
using Sample.Contracts.Employees.Domain;
using Sample.Contracts.Employees.Projections;
using Sample.Domain.Employees.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Employees;

/// <summary>
/// Consumer-facing coverage of the tenant-scoped Employee aggregate living beside the
/// public Order aggregate: with tenancy turned on through
/// <see cref="EdictTestAppBuilder.WithTenancy"/>, the in-memory <see cref="EdictTestApp"/>
/// drives "act as Acme" through <see cref="EdictTestApp.RunAsTenant"/> and asserts
/// only through the consumer read surface that the directory a business sees is its
/// own, that a cross-tenant read is empty by construction, and that the establishing
/// crossing mints a tenant a business can then read.
/// </summary>
public sealed class EmployeeTenantIsolationTests
{
    static readonly EdictTenantId Acme = EdictTenantId.Of("acme");
    static readonly EdictTenantId Globex = EdictTenantId.Of("globex");

    [Fact]
    public async Task RunAsTenant_ShouldSwapTheVisibleDirectory_BetweenTwoCompanies()
    {
        // Arrange
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(EmployeeCommandHandler).Assembly)
            .WithTenancy());

        // Act — each company's employees are established under its own wall through
        // the explicit crossing, so the two directories are populated independently.
        await app.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Alice Adams", "Engineering"), Acme);
        await app.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Bob Brown", "Sales"), Acme);
        await app.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Carol Clark", "Finance"), Globex);
        await app.Drain();

        // Act — the same ambient-scoped query answers with each caller's own partition.
        app.RunAsTenant(Acme);
        var acmeDirectory = await app.QueryMyTenantPartitionAsync<EmployeeDirectoryRow>();

        app.RunAsTenant(Globex);
        var globexDirectory = await app.QueryMyTenantPartitionAsync<EmployeeDirectoryRow>();

        // Assert — switching the tenant swaps one populated list for a different one.
        Assert.Equal(
            ["Alice Adams", "Bob Brown"],
            acmeDirectory.Select(row => row.Name).OrderBy(name => name));
        Assert.Equal(
            ["Carol Clark"],
            globexDirectory.Select(row => row.Name));
    }

    [Fact]
    public async Task QueryMyTenantPartition_ShouldReturnEmpty_ForATenantThatOwnsNoEmployees()
    {
        // Arrange
        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(EmployeeCommandHandler).Assembly)
            .WithTenancy());

        await app.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Alice Adams", "Engineering"), Acme);
        await app.Drain();

        // Act — a different company runs the identical query under its own wall.
        app.RunAsTenant(Globex);
        var globexDirectory = await app.QueryMyTenantPartitionAsync<EmployeeDirectoryRow>();

        // Assert — the cross-tenant read is empty by construction, not by a permission
        // check: Globex composes its own partition and there is nothing there.
        Assert.Empty(globexDirectory);
    }

    [Fact]
    public async Task EstablishingCrossing_ShouldMintATenantWhoseOwnAuditTrailItReadsBack_WhileAnotherTenantSeesNone()
    {
        // Arrange
        var employeeId = new EmployeeId(Guid.NewGuid());

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(EmployeeCommandHandler).Assembly)
            .WithAudit()
            .WithTenancy());

        // Act — the explicit public-to-tenant crossing mints the Acme wall and captures
        // the decision under it.
        await app.SendAsync(new AddEmployeeCommand(employeeId, "Alice Adams", "Engineering"), Acme);
        await app.Drain();

        // Act — Acme reads its own trail; Globex runs the identical principal query.
        app.RunAsTenant(Acme);
        var acmeTrail = await app.TenantAudit.ByPrincipalAsync(
            EdictTestApp.DefaultPrincipal, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        app.RunAsTenant(Globex);
        var globexTrail = await app.TenantAudit.ByPrincipalAsync(
            EdictTestApp.DefaultPrincipal, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        // Assert — Acme sees the crossing's command and event records, every one stamped
        // with its own tenant; Globex sees none of them.
        Assert.NotEmpty(acmeTrail);
        Assert.All(acmeTrail, record => Assert.Equal(Acme, record.Tenant));
        Assert.Empty(globexTrail);
    }
}
