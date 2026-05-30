using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;

namespace Edict.Benchmarks.Throughput.ClosedLoop;

/// <summary>
/// Exercises the aggregate write path in isolation. Sends
/// <see cref="BenchIncrementCommand"/>; the handler increments durable
/// state and returns <c>Accepted</c> without <c>Raise</c> — no Outbox,
/// no stream hop, no projection. The latency captured here is the cost of
/// one Send + the framework's per-command state-provider commit.
/// </summary>
public sealed class CommandsScenario : IClosedLoopScenario
{
    readonly IEdictSender _sender;

    public CommandsScenario(IEdictSender sender)
    {
        _sender = sender;
    }

    public string Name => "Command acceptance";

    public Task IssueOnceAsync(Guid aggregateId, byte[] filler, CancellationToken cancellationToken) =>
        _sender.SendAsync(new BenchIncrementCommand(aggregateId, filler));
}
