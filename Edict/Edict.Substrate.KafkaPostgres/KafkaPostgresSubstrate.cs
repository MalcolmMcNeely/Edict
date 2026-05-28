using Confluent.Kafka;

using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Kafka;
using Edict.Postgres;
using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

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

    public string Name => "kafkapostgres";

    public async Task<ISubstrateRuntime> StartAsync(CancellationToken ct, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop)
    {
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
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
        // Saturation pass measures count-at-window-end on a fresh consumer
        // group; Latest avoids replaying warmup-window backlog into the
        // measurement, which would inflate EPS. Closed-loop keeps Earliest so
        // fresh-group consumers replay deterministically from offset 0.
        var autoOffsetReset = mode == SubstrateStartMode.Saturation
            ? AutoOffsetReset.Latest
            : AutoOffsetReset.Earliest;

        return new KafkaPostgresSubstrateRuntime(
            postgresContainer,
            kafkaContainer,
            postgresConnectionString,
            bootstrapServers,
            consumerGroupId,
            autoOffsetReset);
    }
}

public sealed class KafkaPostgresSubstrateRuntime : ISubstrateRuntime
{
    readonly PostgreSqlContainer _postgresContainer;
    readonly KafkaContainer _kafkaContainer;
    readonly NpgsqlDataSource _dataSource;

    internal KafkaPostgresSubstrateRuntime(
        PostgreSqlContainer postgresContainer,
        KafkaContainer kafkaContainer,
        string postgresConnectionString,
        string bootstrapServers,
        string consumerGroupId,
        AutoOffsetReset kafkaAutoOffsetReset = AutoOffsetReset.Earliest)
    {
        _postgresContainer = postgresContainer;
        _kafkaContainer = kafkaContainer;
        _dataSource = new NpgsqlDataSourceBuilder(postgresConnectionString).Build();
        PostgresConnectionString = postgresConnectionString;
        BootstrapServers = bootstrapServers;
        ConsumerGroupId = consumerGroupId;
        KafkaAutoOffsetReset = kafkaAutoOffsetReset;

        var dataSource = _dataSource;

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
                o.AutoOffsetReset = kafkaAutoOffsetReset;
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
            client.Services.AddSingleton(dataSource);
            client.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(sp =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    KafkaPostgresSubstrate.DeadLetterTableName,
                    sp.GetRequiredService<Serializer>()));
        };
    }

    public string PostgresConnectionString { get; }

    public string BootstrapServers { get; }

    public string ConsumerGroupId { get; }

    /// <summary>
    /// Resolved <see cref="AutoOffsetReset"/> the runtime hands to
    /// <c>AddEdictKafkaStreams</c>. Surfaced so the harness (and tests) can
    /// confirm <see cref="SubstrateStartMode.Saturation"/> mapped to
    /// <see cref="AutoOffsetReset.Latest"/> without reaching into the silo's
    /// service provider.
    /// </summary>
    public AutoOffsetReset KafkaAutoOffsetReset { get; }

    public Action<ISiloBuilder> ConfigureSilo { get; }

    public Action<IClientBuilder> ConfigureClient { get; }

    public IEdictTableRepository<TRow> CreateRowRepository<TRow>(IServiceProvider sp, string tableName)
        where TRow : class, new()
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new PostgresTableRepository<TRow>(
            _dataSource,
            tableName,
            sp.GetRequiredService<Serializer>());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgresContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
