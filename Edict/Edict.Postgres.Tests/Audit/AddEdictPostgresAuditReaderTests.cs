using Edict.Contracts.Audit;
using Edict.Postgres;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;

using Xunit;

namespace Edict.Postgres.Tests.Audit;

// Pins the client-side wiring a non-silo Postgres process (the Sample web host)
// uses to read the audit log: registering the stores against a connection string
// and resolving an IEdictAuditRepository over them. The data source builds lazily,
// so the registration resolves without a live database.
public sealed class AddEdictPostgresAuditReaderTests
{
    [Fact]
    public void AddEdictPostgresAuditReader_ShouldResolveAnAuditRepository()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSerializer();

        // Act
        services.AddEdictPostgresAuditReader(options =>
            options.ConnectionString = "Host=localhost;Database=edict;Username=edict;Password=edict");

        // Assert
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IEdictAuditRepository>());
    }
}
