using System.Diagnostics;

using Confluent.Kafka;

using Testcontainers.Kafka;

namespace Edict.Kafka.Tests;

/// <summary>
/// Assembly-scoped Kafka testcontainer (confluentinc/cp-kafka via
/// Testcontainers.Kafka). Each fixture mints its own consumer group so the
/// shared container can host multiple parallel fixture runs without
/// cross-fixture offset contamination.
/// </summary>
static class KafkaAssemblyHost
{
    static readonly Lazy<Task<KafkaContainer>> _container =
        new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    static KafkaAssemblyHost()
    {
        AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
        {
            if (_container.IsValueCreated)
            {
                try
                {
                    var container = await _container.Value;
                    await container.DisposeAsync();
                }
                catch
                {
                    // Best-effort teardown.
                }
            }
        };
    }

    public static async Task<string> GetBootstrapServersAsync()
    {
        var container = await _container.Value;
        return StripScheme(container.GetBootstrapAddress());
    }

    static async Task<KafkaContainer> StartAsync()
    {
        var container = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
        await container.StartAsync();
        // Testcontainers' Kafka wait strategy keys off an in-container log line,
        // so the container is reported ready while the broker is still settling
        // (fresh listeners, KRaft controller election). A silo's
        // EdictKafkaTopicProvisioner calls AdminClient.GetMetadata the moment the
        // first fixture brings it up, and on a cold broker that races to a
        // "Local: Broker transport failure". The old cross-battery fixtures only
        // dodge it because they create a Postgres database first; the streaming
        // battery touches Kafka with no preceding delay. Proving one metadata
        // round-trip here makes the broker host-ready for every fixture.
        await WaitForKafkaReadyAsync(StripScheme(container.GetBootstrapAddress()));
        return container;
    }

    static async Task WaitForKafkaReadyAsync(string bootstrapServers)
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
                    $"Kafka container reported ready, but the host could not complete an AdminClient.GetMetadata round-trip against '{bootstrapServers}' within {deadline.TotalSeconds:F0} s.",
                    lastError);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    static string StripScheme(string address) =>
        address.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? address.Substring("PLAINTEXT://".Length)
            : address;
}
