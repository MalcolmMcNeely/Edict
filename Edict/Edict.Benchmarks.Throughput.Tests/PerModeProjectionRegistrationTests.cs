using Azure;
using Azure.Data.Tables;

using Edict.Benchmarks.Throughput.Cluster;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Projections;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;
using Edict.Substrate.Azurite;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Benchmarks.Throughput.Tests;

/// <summary>
/// Pins the per-mode projection-registration contract that closes the
/// doubled-projection bug. Each <see cref="SubstrateStartMode"/> activates
/// exactly one of the three projection grains and removes the other two, so one
/// pass never contaminates another's consumer write pressure: the closed-loop
/// cluster activates only <see cref="BenchProjectionBuilder"/>, the
/// <see cref="SubstrateStartMode.SaturationList"/> cluster only
/// <see cref="BenchCounterListProjectionBuilder"/>, and the
/// <see cref="SubstrateStartMode.SaturationState"/> cluster only the in-grain
/// <see cref="BenchCounterStateProjectionBuilder"/>. One fact per mode, asserting
/// the non-active projections' tables stay empty after the active projection has
/// drained. The State pass writes no table at all, so its activation is observed
/// through the projection reader instead. Tables are read directly through the
/// Azurite substrate's <see cref="TableServiceClient"/> (registered on the client
/// by <see cref="AzuriteSubstrateRuntime"/>) because the harness no longer
/// registers the non-active <see cref="IEdictTableWriteStore{TRow}"/>.
/// </summary>
public sealed class PerModeProjectionRegistrationTests
{
    static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    static readonly TimeSpan EmptySettleWindow = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ClosedLoopMode_DoesNotActivateCounterProjections()
    {
        var substrate = new AzuriteSubstrate();
        await ClusterHarness.RunAsync(substrate, SubstrateStartMode.ClosedLoop, async cluster =>
        {
            var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
            var eventRepository = cluster.Client.ServiceProvider
                .GetRequiredService<IEdictTableWriteStore<BenchEventRow>>();
            var tableClient = cluster.Client.ServiceProvider
                .GetRequiredService<TableServiceClient>();

            var aggregateId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            await sender.SendAsync(new BenchPublishCommand(aggregateId, correlationId, []));

            await WaitForRowAsync(
                eventRepository,
                aggregateId.ToString(),
                correlationId.ToString("D"),
                CancellationToken.None);

            await Task.Delay(EmptySettleWindow);

            await AssertTableEmptyForAggregateAsync(
                tableClient.GetTableClient(BenchCounterListProjectionBuilder.TableNameLiteral),
                aggregateId);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaturationListMode_DoesNotActivateBenchProjectionBuilder()
    {
        var substrate = new AzuriteSubstrate();
        await ClusterHarness.RunAsync(substrate, SubstrateStartMode.SaturationList, async cluster =>
        {
            var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
            var counterRepository = cluster.Client.ServiceProvider
                .GetRequiredService<IEdictTableWriteStore<BenchCounterRow>>();
            var tableClient = cluster.Client.ServiceProvider
                .GetRequiredService<TableServiceClient>();

            var aggregateId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            await sender.SendAsync(new BenchPublishCommand(aggregateId, correlationId, []));

            await WaitForStoreCounterAsync(counterRepository, aggregateId, CancellationToken.None);

            await Task.Delay(EmptySettleWindow);

            await AssertTableEmptyForAggregateAsync(
                tableClient.GetTableClient(BenchProjectionBuilder.TableNameLiteral),
                aggregateId);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaturationStateMode_ActivatesOnlyTheInGrainCounter_AndLeavesListTablesEmpty()
    {
        var substrate = new AzuriteSubstrate();
        await ClusterHarness.RunAsync(substrate, SubstrateStartMode.SaturationState, async cluster =>
        {
            var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
            var stateReader = cluster.Client.ServiceProvider
                .GetRequiredService<IEdictProjectionReader<BenchCounterProjection>>();
            var tableClient = cluster.Client.ServiceProvider
                .GetRequiredService<TableServiceClient>();

            var aggregateId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();

            await sender.SendAsync(new BenchPublishCommand(aggregateId, correlationId, []));

            // The in-grain counter writes no table, so its activation is observed
            // through the reader: the per-aggregate payload reaches Count == 1.
            await WaitForStateCounterAsync(stateReader, aggregateId, CancellationToken.None);

            await Task.Delay(EmptySettleWindow);

            // Neither List projection ran — the closed-loop event table and the
            // List counter table both stay empty (and the State pass itself writes
            // no table at all).
            await AssertTableEmptyForAggregateAsync(
                tableClient.GetTableClient(BenchProjectionBuilder.TableNameLiteral),
                aggregateId);
            await AssertTableEmptyForAggregateAsync(
                tableClient.GetTableClient(BenchCounterListProjectionBuilder.TableNameLiteral),
                aggregateId);
            return 0;
        }, CancellationToken.None);
    }

    static async Task WaitForRowAsync<TRow>(
        IEdictTableWriteStore<TRow> repository,
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken)
        where TRow : class, new()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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

    static async Task WaitForStoreCounterAsync(
        IEdictTableWriteStore<BenchCounterRow> repository,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DrainTimeout);
        while (!cts.IsCancellationRequested)
        {
            var row = await repository.GetAsync(
                aggregateId.ToString(),
                BenchCounterListProjectionBuilder.FixedRowKey,
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

    static async Task WaitForStateCounterAsync(
        IEdictProjectionReader<BenchCounterProjection> reader,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DrainTimeout);
        while (!cts.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(aggregateId, cancellationToken: cts.Token);
            if (read.Value is { Count: > 0 })
            {
                return;
            }
            await Task.Delay(PollInterval, cts.Token);
        }
        throw new TimeoutException(
            $"Expected in-grain counter for aggregate {aggregateId} did not reach Count > 0 within {DrainTimeout.TotalSeconds:F0}s.");
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
                    $"Expected no rows in {table.Name} for aggregate {aggregateId} (non-active projection should not have activated). Found row with RowKey={entity.RowKey}.");
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // Table never created — the non-active projection genuinely never ran. Empty by construction.
        }
    }
}
