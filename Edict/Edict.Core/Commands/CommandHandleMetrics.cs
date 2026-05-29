using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Commands;
using Edict.Telemetry;

namespace Edict.Core.Commands;

static class CommandHandleMetrics
{
    static readonly Histogram<double> HandleDuration = EdictDiagnostics.Meter.CreateHistogram<double>(
        SemanticConventions.Commands.Meters.HandleDuration);

    public static async Task<EdictCommandResult> RunAndRecordAsync<TCommand>(
        Func<Task<EdictCommandResult>> handle, string grainTypeName)
        where TCommand : EdictCommand
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return await handle();
        }
        finally
        {
            HandleDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object?>(SemanticConventions.Commands.Tags.Type, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, grainTypeName));
        }
    }
}
