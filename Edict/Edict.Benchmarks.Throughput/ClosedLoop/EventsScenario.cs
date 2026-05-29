using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;

namespace Edict.Benchmarks.Throughput.ClosedLoop;

/// <summary>
/// End-to-end pipeline scenario. Mints a per-Send <c>CorrelationId</c>,
/// sends <see cref="BenchPublishCommand"/>, then polls the projection row
/// written by <c>BenchProjectionBuilder</c> at <c>(aggregateId,
/// correlationId)</c>. The row's existence is the substrate-neutral
/// completion signal — point-get against the projection store, polled at
/// ~5 ms. A fresh <c>CorrelationId</c> per call prevents the wait from
/// short-circuiting on a stale row from a previous send.
/// </summary>
public sealed class EventsScenario : IClosedLoopScenario
{
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    readonly IEdictSender _sender;
    readonly IEdictTableRepository<BenchEventRow> _rowRepository;

    public EventsScenario(IEdictSender sender, IEdictTableRepository<BenchEventRow> rowRepository)
    {
        _sender = sender;
        _rowRepository = rowRepository;
    }

    public string Name => "Command → Event delivery";

    public async Task IssueOnceAsync(Guid aggregateId, byte[] filler, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        await _sender.Send(new BenchPublishCommand(aggregateId, correlationId, filler));
        await WaitForEventRowAsync(aggregateId, correlationId.ToString("D"), cancellationToken);
    }

    async Task WaitForEventRowAsync(Guid aggregateId, string rowKey, CancellationToken cancellationToken)
    {
        var partitionKey = aggregateId.ToString();
        while (!cancellationToken.IsCancellationRequested)
        {
            var row = await _rowRepository.GetAsync(partitionKey, rowKey, cancellationToken);
            if (row is not null)
            {
                return;
            }
            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
