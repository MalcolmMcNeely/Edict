using Azure.Data.Tables;

using Edict.Benchmarks.Throughput;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class AzuriteSubstrateTests
{
    [Fact]
    public async Task Runtime_SiloCallback_ResolvesTableRepositoryThatReadsBackPreloadedRow()
    {
        var substrate = new AzuriteSubstrate();
        await using var runtime = (AzuriteSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

        // Pre-seed a row directly through Azurite — substrate's persistence
        // wiring will register the repository against the same table.
        var preloadTable = new TableServiceClient(runtime.ConnectionString)
            .GetTableClient(AzuriteSubstrate.DeadLetterTableName);
        await preloadTable.CreateIfNotExistsAsync();
        var partitionKey = Guid.NewGuid().ToString("N");
        var rowKey = Guid.NewGuid().ToString("N");
        await preloadTable.AddEntityAsync(new TableEntity(partitionKey, rowKey)
        {
            { nameof(EdictDeadLetterEntry.EntryId), Guid.NewGuid() },
            { nameof(EdictDeadLetterEntry.Kind), "PublishEvent" },
            { nameof(EdictDeadLetterEntry.AttemptCount), 3 },
            { nameof(EdictDeadLetterEntry.DeadLetteredAt), DateTimeOffset.UtcNow },
            { nameof(EdictDeadLetterEntry.SourceGrainKey), "grain-key" },
            { nameof(EdictDeadLetterEntry.SourceGrainType), "Grain.Type" },
            { nameof(EdictDeadLetterEntry.EffectTarget), "stream/EventType" },
            { nameof(EdictDeadLetterEntry.FailureKind), (int)EdictDeadLetterFailureKind.EffectFailure },
        });

        ActiveSubstrateRuntime.Current = runtime;
        try
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<ActiveSubstrateRuntime.SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ActiveSubstrateRuntime.ClientConfigurator>();
            var cluster = builder.Build();
            await cluster.DeployAsync();
            try
            {
                var repository = cluster.ServiceProvider
                    .GetRequiredService<IEdictTableRepository<EdictDeadLetterEntry>>();

                var row = await repository.GetAsync(partitionKey, rowKey);

                Assert.NotNull(row);
                Assert.Equal("PublishEvent", row!.Kind);
                Assert.Equal(3, row.AttemptCount);
                Assert.Equal("grain-key", row.SourceGrainKey);
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            ActiveSubstrateRuntime.Current = null;
        }
    }

    static class ActiveSubstrateRuntime
    {
        public static ISubstrateRuntime? Current { get; set; }

        public sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException("Substrate runtime not set");
                runtime.ConfigureSilo(siloBuilder);
            }
        }

        public sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(
                Microsoft.Extensions.Configuration.IConfiguration configuration,
                IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException("Substrate runtime not set");
                runtime.ConfigureClient(clientBuilder);
            }
        }
    }
}
