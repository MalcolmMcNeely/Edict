using System.Diagnostics;

using Edict.Contracts.Commands;
using Edict.Contracts.Results;

using Orleans.Runtime;

namespace Edict.Core.Diagnostics;

/// <summary>
/// Opens and closes the single Edict command span around handler execution.
/// Sets the route-key Guid as the correlation tag before the handler runs so
/// it is visible on error spans. Records <see cref="ActivityStatusCode.Error"/>
/// if the handler throws, then re-throws. Has no Orleans dependency.
/// </summary>
internal static class CommandSpanScope
{
    internal static async Task<CommandResult> ExecuteAsync(
        string operationName,
        Guid routeKey,
        Action<Command, Activity?>? tagWriter,
        Command command,
        Func<Task<CommandResult>> handler)
    {
        using var activity = EdictDiagnostics.ActivitySource.StartActivity(operationName);

        if (activity is not null)
        {
            activity.SetTag("edict.command.route_key", routeKey);
            tagWriter?.Invoke(command, activity);
            RequestContext.Set(EdictDiagnostics.TraceIdKey, activity.TraceId.ToHexString());
            RequestContext.Set(EdictDiagnostics.SpanIdKey, activity.SpanId.ToHexString());
            if (activity.TraceStateString is { } traceState)
                RequestContext.Set(EdictDiagnostics.TraceStateKey, traceState);
        }

        try
        {
            return await handler();
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
    }
}
