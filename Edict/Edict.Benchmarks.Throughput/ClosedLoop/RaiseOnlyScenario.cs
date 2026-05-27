using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;

namespace Edict.Benchmarks.Throughput.ClosedLoop;

/// <summary>
/// Producer-side diagnostic between Commands and Events. Sends
/// <see cref="BenchPublishCommand"/>; the handler <c>Raise</c>s one
/// <c>BenchEvent</c>, returning once the atomic
/// <c>{State, Outbox}</c> commit completes. The issuer does not wait for
/// the consumer to dispatch — so per-Send latency captures the producer-
/// side <c>Raise</c> cost but excludes stream-dispatch + projection time.
/// </summary>
public sealed class RaiseOnlyScenario : IClosedLoopScenario
{
    readonly IEdictSender _sender;

    public RaiseOnlyScenario(IEdictSender sender)
    {
        _sender = sender;
    }

    public string Name => "RaiseOnly";

    public Task IssueOnceAsync(Guid aggregateId, byte[] filler, CancellationToken ct) =>
        _sender.Send(new BenchPublishCommand(aggregateId, Guid.NewGuid(), filler));
}
