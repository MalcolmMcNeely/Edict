using System.Diagnostics.CodeAnalysis;

using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Commands;
using Edict.Contracts.Sending;

namespace Edict.Benchmarks.Throughput.ClosedLoop;

/// <summary>
/// A/B counterpart to <see cref="CommandsScenario"/>: the argument is typed
/// as <see cref="EdictCommand"/> at the call site, which
/// gives this Send a different syntactic source location than the typed
/// scenario's site. The generator's per-type <c>[InterceptsLocation]</c>
/// only matches the typed location, so this scenario always pays the
/// registrar dictionary lookup + route-key delegate hop — regardless of
/// the <c>EdictInterceptorsEnabled</c> build property. The throughput
/// delta against <see cref="CommandsScenario"/> is the perf claim that
/// justifies shipping interceptors.
/// </summary>
public sealed class CommandsBaseTypedScenario : IClosedLoopScenario
{
    readonly IEdictSender _sender;

    public CommandsBaseTypedScenario(IEdictSender sender)
    {
        _sender = sender;
    }

    public string Name => "Command acceptance (base-typed)";

    [SuppressMessage(
        "Edict", "EDICT015",
        Justification = "Interceptor A/B counterpart — deliberately base-typed to force the slow path.")]
    public Task IssueOnceAsync(Guid aggregateId, byte[] filler, CancellationToken cancellationToken)
    {
        EdictCommand command = new BenchIncrementCommand(aggregateId, filler);
        return _sender.Send(command);
    }
}
