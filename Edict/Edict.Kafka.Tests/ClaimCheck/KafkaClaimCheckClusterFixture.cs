using Confluent.Kafka;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Wire;
using Edict.Postgres;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.ClaimCheck;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Kafka.Tests.ClaimCheck;

/// <summary>
/// Conformance fixture for the claim-check mix-and-match case
/// "Kafka streams + Postgres-backed claim-check store". The Postgres
/// persistence provider registers <c>PostgresClaimCheckStore</c> as
/// <c>IEdictClaimCheckStore</c>; the silo's <c>ClaimCheckPolicy</c> is
/// re-bound at a 1-byte threshold AFTER persistence so the real Postgres
/// store backs every pointer-branch publish. Probing "blob exists" maps to
/// "row exists in <c>edict_claim_check</c>" (same shape as
/// <c>PostgresClaimCheckClusterFixture</c>).
/// </summary>
public sealed class KafkaClaimCheckClusterFixture : ClaimCheckFixture
{
    string _adminConnectionString = "";
    string _databaseConnectionString = "";
    string _bootstrapServers = "";
    string _consumerGroup = "";
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public string DeadLetterTableName { get; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; } = "edict_claim_check";

    public override IEdictTableRepository<T> GetTableRepository<T>(string tableName) =>
        new PostgresTableRepository<T>(
            _databaseConnectionString,
            tableName,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override IEdictTableStoreFactory TableStoreFactory =>
        new PostgresTableWriteStoreFactory(
            _databaseConnectionString,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override async Task<bool> ClaimCheckBlobExistsAsync(string key)
    {
        if (!Guid.TryParseExact(key, "N", out var id))
        {
            return false;
        }
        await using var connection = new NpgsqlConnection(_databaseConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM edict_claim_check WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    public override async Task InitializeAsync()
    {
        _adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        _bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        _consumerGroup = $"edict-kafka-cc-{Guid.NewGuid():N}";

        var databaseName = $"edict_{Guid.NewGuid():N}";
        _databaseConnectionString =
            await PostgresDatabaseFactory.CreateDatabaseAsync(_adminConnectionString, databaseName);

        var context = new KafkaClusterContext(
            _bootstrapServers,
            _consumerGroup,
            _databaseConnectionString,
            DeadLetterTableName,
            ClaimCheckTableName,
            databaseName);
        _contextKey = KafkaClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[KafkaClusterContextRegistry.ContextKeyProperty] = _contextKey;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public override async Task DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
        KafkaClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(ClaimCheckCounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(EdictKafkaWireEnvelope).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[KafkaClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from silo configuration.");
            var ctx = KafkaClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.KafkaBootstrapServers;
                o.ConsumerGroupId = ctx.KafkaConsumerGroup;
                o.PartitionCount = 4;
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            });
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.PostgresConnectionString;
                o.DeadLetterTableName = ctx.DeadLetterTableName;
                o.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });
            // Re-bind ClaimCheckPolicy at 1-byte AFTER persistence so the
            // Postgres claim-check store is the backing IEdictClaimCheckStore
            // every pointer-branch publish writes to (same trick as
            // PostgresClaimCheckClusterFixture).
            siloBuilder.Services.AddSingleton(sp => new ClaimCheckPolicy(
                sp.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: sp.GetRequiredService<IEdictClaimCheckStore>()));
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaClaimCheckCollection : ICollectionFixture<KafkaClaimCheckClusterFixture>
{
    public const string Name = "KafkaClaimCheck";
}
