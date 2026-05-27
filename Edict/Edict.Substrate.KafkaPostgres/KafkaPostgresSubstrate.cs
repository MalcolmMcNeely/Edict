using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Kafka;
using Edict.Postgres;
using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.Serialization;

using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace Edict.Substrate.KafkaPostgres;

/// <summary>
/// Brings up a Postgres + Kafka pair and hands back ConfigureSilo /
/// ConfigureClient callbacks wiring
/// <see cref="EdictKafkaSiloBuilderExtensions.AddEdictKafkaStreams"/> against
/// the Kafka broker and
/// <see cref="EdictPostgresSiloBuilderExtensions.AddEdictPostgresPersistence"/>
/// against the Postgres instance. Each runtime mints its own consumer group so
/// concurrent <see cref="StartAsync"/> calls (parallel test fixtures) do not
/// collide on offsets.
/// </summary>
public sealed class KafkaPostgresSubstrate : ISubstrate
{
    public const string DeadLetterTableName = "edict_dead_letter";

    // Tracer-bullet partition count matches the conformance fixture
    // (KafkaClusterFixture) so substrate-driven runs see the same partition
    // → stream-key mapping the slice-1/2 conformance battery proves.
    const int PartitionCount = 4;

    public string Name => "kafkapostgres";

    public async Task<ISubstrateRuntime> StartAsync(CancellationToken ct)
    {
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        var kafkaContainer = new KafkaBuilder().Build();
        await Task.WhenAll(postgresContainer.StartAsync(ct), kafkaContainer.StartAsync(ct));

        var postgresConnectionString = postgresContainer.GetConnectionString();
        var bootstrapAddress = kafkaContainer.GetBootstrapAddress();
        // Confluent.Kafka clients reject the "PLAINTEXT://" scheme prefix —
        // matches the strip in Edict.Kafka.Tests/KafkaAssemblyHost.
        var bootstrapServers = bootstrapAddress.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? bootstrapAddress["PLAINTEXT://".Length..]
            : bootstrapAddress;
        var consumerGroupId = $"edict-substrate-{Guid.NewGuid():N}";

        return new KafkaPostgresSubstrateRuntime(
            postgresContainer,
            kafkaContainer,
            postgresConnectionString,
            bootstrapServers,
            consumerGroupId);
    }
}

public sealed class KafkaPostgresSubstrateRuntime : ISubstrateRuntime
{
    readonly PostgreSqlContainer _postgresContainer;
    readonly KafkaContainer _kafkaContainer;

    internal KafkaPostgresSubstrateRuntime(
        PostgreSqlContainer postgresContainer,
        KafkaContainer kafkaContainer,
        string postgresConnectionString,
        string bootstrapServers,
        string consumerGroupId)
    {
        _postgresContainer = postgresContainer;
        _kafkaContainer = kafkaContainer;
        PostgresConnectionString = postgresConnectionString;
        BootstrapServers = bootstrapServers;
        ConsumerGroupId = consumerGroupId;

        ConfigureSilo = silo =>
        {
            silo.Services.AddSerializer(s => s
                .AddAssembly(typeof(KafkaPostgresSubstrate).Assembly)
                .AddEdictContractSerializer());
            silo.AddEdict();
            silo.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = bootstrapServers;
                o.ConsumerGroupId = consumerGroupId;
                o.PartitionCount = 4;
            });
            silo.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = postgresConnectionString;
                o.DeadLetterTableName = KafkaPostgresSubstrate.DeadLetterTableName;
            });
        };

        ConfigureClient = client =>
        {
            client.Services.AddSerializer(s => s
                .AddAssembly(typeof(KafkaPostgresSubstrate).Assembly)
                .AddEdictContractSerializer());
            client.Services.AddEdict();
            client.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(sp =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    postgresConnectionString,
                    KafkaPostgresSubstrate.DeadLetterTableName,
                    sp.GetRequiredService<Serializer>()));
        };
    }

    public string PostgresConnectionString { get; }

    public string BootstrapServers { get; }

    public string ConsumerGroupId { get; }

    public Action<ISiloBuilder> ConfigureSilo { get; }

    public Action<IClientBuilder> ConfigureClient { get; }

    public async ValueTask DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
