using Edict.Contracts.Commands;
using Edict.Core.Commands;

namespace Edict.Benchmarks.Throughput.Workload;

public partial class BenchAggregateHandler : EdictCommandHandler<BenchAggregateState>
{
    public async Task<EdictCommandResult> Handle(BenchIncrementCommand command)
    {
        // The Commands scenario exists to measure the aggregate-write path. The
        // framework's atomic commit only fires when a handler Raises; we
        // deliberately don't Raise here (would bring in the Outbox + stream
        // hop), so we write state ourselves. The Events scenario in a later
        // slice exercises the Raise path end-to-end.
        State.Count++;
        await WriteStateAsync();
        return new EdictCommandResult.Accepted();
    }
}
