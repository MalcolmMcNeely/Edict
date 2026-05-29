using System.Diagnostics;

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

    public async Task<ISubstrateRuntime> StartAsync(CancellationToken cancellationToken, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop)
    {
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            // Postgres ships max_connections=100. The bench silo opens up to
            // EdictPostgresPersistenceOptions.MaxPoolSize=200 (ADR-0035) on
            // its dedicated DataSource, plus Orleans PubSubStore + Reminders
            // each get their own AdoNet pool (~100 default), plus the client-
            // side substrate DataSource below (~100). The harness pins
            // InitialSilosCount=1 (see ClusterHarness), so peak demand is
            // 200 + 100 + 100 + 100 = 500. 1024 fits that with 2× headroom
            // and keeps the operator math from ADR-0035 satisfied
            // ("silos × MaxPoolSize ≤ pg.max_connections").
            .WithCommand("-c", "max_connections=1024")
            .Build();
        var kafkaContainer = new KafkaBuilder().Build();
        await Task.WhenAll(postgresContainer.StartAsync(cancellationToken), kafkaContainer.StartAsync(cancellationToken));

        var postgresConnectionString = postgresContainer.GetConnectionString();
        var bootstrapAddress = kafkaContainer.GetBootstrapAddress();
        // Confluent.Kafka clients reject the "PLAINTEXT://" scheme prefix —
        // matches the strip in Edict.Kafka.Tests/KafkaAssemblyHost.
        var bootstrapServers = bootstrapAddress.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? bootstrapAddress["PLAINTEXT://".Length..]
            : bootstrapAddress;

        await WaitForKafkaReadyAsync(bootstrapServers, cancellationToken);
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

    // Mirrors AzuriteSubstrate.WaitForHostEndpointsAsync: Testcontainers'
    // Kafka wait strategy keys off an in-container log line, so the container
    // is reported ready while the broker is still settling — fresh listeners,
    // KRaft controller election, etc. The silo's EdictKafkaTopicProvisioner
    // immediately calls AdminClient.GetMetadata(10 s), and a TestCluster's 2
    // silos plus their stream-provider pulling agents add up to dozens of
    // simultaneous ApiVersionRequest probes. On a cold broker that storm
    // times out as "Local: Broker transport failure" and aborts silo
    // startup. Waiting for a single successful metadata round-trip here
    // proves the broker can serve the API surface before the silos race.
    static async Task WaitForKafkaReadyAsync(string bootstrapServers, CancellationToken cancellationToken)
    {
        var deadline = TimeSpan.FromSeconds(60);
        var stopwatch = Stopwatch.StartNew();
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
            SocketTimeoutMs = 5_000,
        };

        Exception? lastError = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var admin = new AdminClientBuilder(adminConfig).Build();
                var metadata = admin.GetMetadata(TimeSpan.FromSeconds(5));
                if (metadata.Brokers.Count > 0)
                {
                    return;
                }
                lastError = new InvalidOperationException(
                    "AdminClient.GetMetadata succeeded but returned zero brokers.");
            }
            catch (KafkaException exception)
            {
                lastError = exception;
            }

            if (stopwatch.Elapsed >= deadline)
            {
                throw new InvalidOperationException(
                    $"Kafka container reported ready, but the host could not complete an AdminClient.GetMetadata round-trip against '{bootstrapServers}' within {deadline.TotalSeconds:F0} s. The broker may be stuck in KRaft controller election or the host port-forwarder may not have published the mapping.",
                    lastError);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
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
            client.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(serviceProvider =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                    KafkaPostgresSubstrate.DeadLetterTableName,
                    serviceProvider.GetRequiredService<Serializer>()));
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

    public IEdictTableRepository<TRow> CreateRowRepository<TRow>(IServiceProvider serviceProvider, string tableName)
        where TRow : class, new()
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new PostgresTableRepository<TRow>(
            _dataSource,
            tableName,
            serviceProvider.GetRequiredService<Serializer>());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgresContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
