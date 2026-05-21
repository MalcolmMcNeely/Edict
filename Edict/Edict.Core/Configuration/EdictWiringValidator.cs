using Edict.Contracts.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edict.Core.Configuration;

/// <summary>
/// Host-start validator (ADR 0028): at <see cref="StartAsync"/>, accumulates
/// both <see cref="EdictWiringInspector"/>'s missing-provider list and
/// <see cref="EdictOptionsValidator"/>'s invalid-value list into one
/// <see cref="InvalidOperationException"/>. A consumer with two problems sees
/// two problems; the message lists every issue so the next host restart finds
/// nothing wrong rather than the next-discovered problem.
/// </summary>
public sealed class EdictWiringValidator(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        var markers = services.GetServices<IEdictWiringMarker>();
        problems.AddRange(EdictWiringInspector.Inspect(markers));

        var edictOptions = services.GetService<IOptions<EdictOptions>>();
        if (edictOptions is not null)
        {
            problems.AddRange(EdictOptionsValidator.Validate(edictOptions.Value));
        }

        if (problems.Count > 0)
        {
            var aggregated = string.Join(Environment.NewLine, problems.Select(p => $"  - {p}"));
            throw new InvalidOperationException(
                $"Edict wiring is invalid:{Environment.NewLine}{aggregated}");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
