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
    readonly TimeProvider _timeProvider;
    readonly BringUpTuning _tuning;
    readonly SubstrateBringUpPolicy _bringUpPolicy;

    public KafkaPostgresSubstrate()
        : this(TimeProvider.System, BringUpTuning.FromEnvironment())
    {
    }

    public KafkaPostgresSubstrate(TimeProvider timeProvider, BringUpTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(tuning);
        _timeProvider = timeProvider;
        _tuning = tuning;
        _bringUpPolicy = new SubstrateBringUpPolicy(timeProvider);
    }

    public string Name => "kafkapostgres";

    public Task<ISubstrateRuntime> StartAsync(CancellationToken cancellationToken, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop) =>
        _bringUpPolicy.BringUpAsync(
            Name,
            [StartContainersAsync],
            disposables => Build((StartedContainerPair)disposables[0], mode),
            _tuning,
            cancellationToken);

    static KafkaPostgresSubstrateRuntime Build(StartedContainerPair started, SubstrateStartMode mode)
    {
        var consumerGroupId = $"edict-substrate-{Guid.NewGuid():N}";
        // Both saturation passes measure count-at-window-end on a fresh
        // consumer group; Latest avoids replaying warmup-window backlog into
        // the measurement, which would inflate EPS. Closed-loop keeps Earliest
        // so fresh-group consumers replay deterministically from offset 0.
        var autoOffsetReset = mode is SubstrateStartMode.SaturationList or SubstrateStartMode.SaturationState
            ? AutoOffsetReset.Latest
            : AutoOffsetReset.Earliest;

        return new KafkaPostgresSubstrateRuntime(
            started.PostgresContainer,
            started.KafkaContainer,
            started.PostgresConnectionString,
            started.BootstrapServers,
            consumerGroupId,
            autoOffsetReset);
    }

    async Task<IAsyncDisposable> StartContainersAsync(BringUpTuning tuning, CancellationToken cancellationToken)
    {
        var postgresContainer = new PostgreSqlBuilder("postgres:17-alpine")
            // Postgres ships max_connections=100. The bench silo opens up to
            // EdictPostgresPersistenceOptions.MaxPoolSize=200 on its
            // dedicated DataSource, plus Orleans PubSubStore + Reminders each
            // get their own AdoNet pool (~100 default), plus the client-side
            // substrate DataSource below (~100). The harness pins
            // InitialSilosCount=1 (see ClusterHarness), so peak demand is
            // 200 + 100 + 100 + 100 = 500. 1024 fits that with 2× headroom
            // and keeps the per-silo pool-size budget satisfied
            // ("silos × MaxPoolSize ≤ pg.max_connections").
            .WithCommand("-c", "max_connections=1024")
            .Build();
        var kafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
        try
        {
            // Postgres and Kafka still start together here; serializing them
            // (Postgres-then-Kafka) so two heavy containers do not fight for
            // CPU and disk is a later within-boot-sequencing slice.
            await Task.WhenAll(postgresContainer.StartAsync(cancellationToken), kafkaContainer.StartAsync(cancellationToken));

            var postgresConnectionString = postgresContainer.GetConnectionString();
            var bootstrapAddress = kafkaContainer.GetBootstrapAddress();
            // Confluent.Kafka clients reject the "PLAINTEXT://" scheme prefix —
            // matches the strip in Edict.Kafka.Tests/KafkaAssemblyHost.
            var bootstrapServers = bootstrapAddress.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
                ? bootstrapAddress["PLAINTEXT://".Length..]
                : bootstrapAddress;

            await WaitForKafkaReadyAsync(bootstrapServers, tuning, cancellationToken);

            return new StartedContainerPair(postgresContainer, kafkaContainer, postgresConnectionString, bootstrapServers);
        }
        catch
        {
            // Release both containers before the retry: a stalled host-port
            // forwarder never clears on the same mapping, so the next attempt
            // must start from freshly created containers (and their disposal
            // keeps a doomed run from leaking Docker/Podman resources).
            await DisposeQuietlyAsync(kafkaContainer);
            await DisposeQuietlyAsync(postgresContainer);
            throw;
        }
    }

    static async Task DisposeQuietlyAsync(IAsyncDisposable container)
    {
        try
        {
            await container.DisposeAsync();
        }
        catch
        {
            // A teardown failure must not mask the bring-up failure that triggered it.
        }
    }

    // Holds the started container pair (plus the values derived from them at
    // start) so the policy owns disposal on a failed attempt while the assembly
    // callback composes the runtime on success.
    sealed class StartedContainerPair(
        PostgreSqlContainer postgresContainer,
        KafkaContainer kafkaContainer,
        string postgresConnectionString,
        string bootstrapServers) : IAsyncDisposable
    {
        public PostgreSqlContainer PostgresContainer { get; } = postgresContainer;

        public KafkaContainer KafkaContainer { get; } = kafkaContainer;

        public string PostgresConnectionString { get; } = postgresConnectionString;

        public string BootstrapServers { get; } = bootstrapServers;

        public async ValueTask DisposeAsync()
        {
            await DisposeQuietlyAsync(KafkaContainer);
            await DisposeQuietlyAsync(PostgresContainer);
        }
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
    async Task WaitForKafkaReadyAsync(string bootstrapServers, BringUpTuning tuning, CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
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

            if (_timeProvider.GetElapsedTime(startTimestamp) >= tuning.HostReadinessProbeDeadline)
            {
                throw new InvalidOperationException(
                    $"Kafka container reported ready, but the host could not complete an AdminClient.GetMetadata round-trip against '{bootstrapServers}' within {tuning.HostReadinessProbeDeadline.TotalSeconds:F0} s. The broker may be stuck in KRaft controller election or the host port-forwarder may not have published the mapping.",
                    lastError);
            }
            await Task.Delay(tuning.HostReadinessProbePollInterval, _timeProvider, cancellationToken);
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
            });
        };

        ConfigureClient = client =>
        {
            client.Services.AddSerializer(s => s
                .AddAssembly(typeof(KafkaPostgresSubstrate).Assembly)
                .AddEdictContractSerializer());
            client.Services.AddEdict();
            client.Services.AddSingleton(dataSource);
            // The dead-letter forensic facade reads through the projection grain
            // now, so AddEdict()'s auto-registered IEdictListProjectionReader serves it
            // with no substrate-side repository wiring.
        };
    }

    public string PostgresConnectionString { get; }

    public string BootstrapServers { get; }

    public string ConsumerGroupId { get; }

    /// <summary>
    /// Resolved <see cref="AutoOffsetReset"/> the runtime hands to
    /// <c>AddEdictKafkaStreams</c>. Surfaced so the harness (and tests) can
    /// confirm either saturation mode mapped to <see cref="AutoOffsetReset.Latest"/>
    /// without reaching into the silo's service provider.
    /// </summary>
    public AutoOffsetReset KafkaAutoOffsetReset { get; }

    public Action<ISiloBuilder> ConfigureSilo { get; }

    public Action<IClientBuilder> ConfigureClient { get; }

    public IEdictTableWriteStore<TRow> CreateRowStore<TRow>(IServiceProvider serviceProvider, string tableName)
        where TRow : class, new()
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new PostgresTableWriteStore<TRow>(
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
