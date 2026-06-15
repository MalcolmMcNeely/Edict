using Edict.Contracts.Audit;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Core.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edict.Core.Configuration;

sealed class EdictWiringValidator(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        var markers = services.GetServices<IEdictWiringMarker>().ToList();
        problems.AddRange(EdictWiringInspector.Inspect(markers));

        var edictOptions = services.GetService<IOptions<EdictOptions>>();
        if (edictOptions is not null)
        {
            problems.AddRange(EdictOptionsValidator.Validate(edictOptions.Value));
        }

        var sagaOptions = services.GetService<IOptions<EdictSagaOptions>>();
        if (sagaOptions is not null)
        {
            problems.AddRange(EdictSagaOptionsValidator.Validate(sagaOptions.Value));
        }

        var scheduleOptions = services.GetService<IOptions<EdictCommandHandlerScheduleOptions>>();
        if (scheduleOptions is not null)
        {
            problems.AddRange(EdictCommandHandlerScheduleOptionsValidator.Validate(scheduleOptions.Value));
        }

        if (markers.Any(m => m is EdictStreamsProviderMarker)
            && services.GetService<IEdictClaimCheckStore>() is null)
        {
            problems.Add(
                "Missing claim-check store: a streams provider is registered but no IEdictClaimCheckStore is. "
                + "Call silo.AddEdictAzureBlobClaimCheck(...) (for the Azure-blob store) "
                + "or silo.AddEdictPostgresPersistence(...) (which registers the Postgres-backed store) "
                + "so oversized events and pointer envelopes have somewhere to land.");
        }

        if (services.GetService<EdictAuditEnabledMarker>() is not null
            && services.GetService<IEdictPrincipalResolver>() is null)
        {
            problems.Add(
                "Auditing is on (WithAudit) but no principal resolver is registered. "
                + "Call services.AddEdictAudit(...) so an originating send can resolve the actor; "
                + "without it every audited send would fail closed at the edge.");
        }

        if (services.GetService<EdictAuditSiloMarker>() is not null
            && services.GetService<IEdictAuditStore>() is null)
        {
            problems.Add(
                "Auditing is on (WithAudit) but no audit store is registered. "
                + "Call silo.AddEdictPostgresPersistence(...) (which registers the Postgres-backed store) "
                + "so captured records have a tamper-evident store to drain to; "
                + "without it every captured decision would be staged but never durable.");
        }

        if (problems.Count > 0)
        {
            var aggregated = string.Join(Environment.NewLine, problems.Select(p => $"  - {p}"));
            throw new EdictWiringException(
                $"Edict wiring is invalid:{Environment.NewLine}{aggregated}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
