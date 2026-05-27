using Azure;
using Azure.Data.Tables;

using Edict.Benchmarks.Throughput.Cluster;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;
using Edict.Substrate.Azurite;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Benchmarks.Throughput.Tests;

/// <summary>
/// Closes the doubled-projection bug ADR-0031 had aspirationally claimed away
/// but the harness wasn't enforcing. Per-<see cref="SubstrateStartMode"/>
/// projection registration means the saturation cluster never activates
/// <see cref="BenchProjectionBuilder"/> and the closed-loop cluster never
/// activates <see cref="BenchCounterProjectionBuilder"/>. Two facts, one per
/// mode, asserting the wrong-mode table is empty after the right-mode row
/// has drained. The wrong-mode table is read directly through the Azurite
/// substrate's <see cref="TableServiceClient"/> (registered on the client by
/// <see cref="AzuriteSubstrateRuntime"/>) because the harness no longer
/// registers the wrong-mode <see cref="IEdictTableRepository{TRow}"/>.
/// </summary>
public sealed class PerModeProjectionRegistrationTests
{
    static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    static readonly TimeSpan EmptySettleWindow = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ClosedLoopMode_DoesNotActivateBenchCounterProjectionBuilder()
    {
        var substrate = new AzuriteSubstrate();
        await ClusterHarness.RunAsync(substrate, SubstrateStartMode.ClosedLoop, async cluster =>
        {
            var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
            var eventRepository = cluster.Client.ServiceProvider
                .GetRequiredService<IEdictTableRepository<BenchEventRow>>();
            var tableClient = cluster.Client.ServiceProvider
                .GetRequiredService<TableServiceClient>();

            var aggregateId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            await sender.Send(new BenchPublishCommand(aggregateId, correlationId, []));

            await WaitForRowAsync(
                eventRepository,
                aggregateId.ToString(),
                correlationId.ToString("D"),
                CancellationToken.None);

            await Task.Delay(EmptySettleWindow);

            await AssertTableEmptyForAggregateAsync(
                tableClient.GetTableClient(BenchCounterProjectionBuilder.TableNameLiteral),
                aggregateId);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaturationMode_DoesNotActivateBenchProjectionBuilder()
    {
        var substrate = new AzuriteSubstrate();
        await ClusterHarness.RunAsync(substrate, SubstrateStartMode.Saturation, async cluster =>
        {
            var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
            var counterRepository = cluster.Client.ServiceProvider
                .GetRequiredService<IEdictTableRepository<BenchCounterRow>>();
            var tableClient = cluster.Client.ServiceProvider
                .GetRequiredService<TableServiceClient>();

            var aggregateId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            await sender.Send(new BenchPublishCommand(aggregateId, correlationId, []));

            await WaitForCounterAsync(counterRepository, aggregateId, CancellationToken.None);

            await Task.Delay(EmptySettleWindow);

            await AssertTableEmptyForAggregateAsync(
                tableClient.GetTableClient(BenchProjectionBuilder.TableNameLiteral),
                aggregateId);
            return 0;
        }, CancellationToken.None);
    }

    static async Task WaitForRowAsync<TRow>(
        IEdictTableRepository<TRow> repository,
        string partitionKey,
        string rowKey,
        CancellationToken ct)
        where TRow : class, new()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DrainTimeout);
        while (!cts.IsCancellationRequested)
        {
            var row = await repository.GetAsync(partitionKey, rowKey, cts.Token);
            if (row is not null)
            {
                return;
            }
            await Task.Delay(PollInterval, cts.Token);
        }
        throw new TimeoutException(
            $"Expected row at ({partitionKey}, {rowKey}) did not appear within {DrainTimeout.TotalSeconds:F0}s.");
    }

    static async Task WaitForCounterAsync(
        IEdictTableRepository<BenchCounterRow> repository,
        Guid aggregateId,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DrainTimeout);
        while (!cts.IsCancellationRequested)
        {
            var row = await repository.GetAsync(
                aggregateId.ToString(),
                BenchCounterProjectionBuilder.FixedRowKey,
                cts.Token);
            if (row is { Count: > 0 })
            {
                return;
            }
            await Task.Delay(PollInterval, cts.Token);
        }
        throw new TimeoutException(
            $"Expected counter row for aggregate {aggregateId} did not appear within {DrainTimeout.TotalSeconds:F0}s.");
    }

    static async Task AssertTableEmptyForAggregateAsync(TableClient table, Guid aggregateId)
    {
        try
        {
            var partitionKey = aggregateId.ToString();
            await foreach (var entity in table.QueryAsync<TableEntity>(
                e => e.PartitionKey == partitionKey))
            {
                Assert.Fail(
                    $"Expected no rows in {table.Name} for aggregate {aggregateId} (wrong-mode projection should not have activated). Found row with RowKey={entity.RowKey}.");
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Table never created — the wrong-mode projection genuinely never ran. Empty by construction.
        }
    }
}
