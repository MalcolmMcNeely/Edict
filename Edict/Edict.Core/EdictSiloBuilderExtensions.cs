using Edict.Contracts.Configuration;
using Edict.Core.Configuration;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edict.Core;

/// <summary>
/// Silo-side installation surface. Three <see cref="ISiloBuilder"/>
/// extensions replace the previous seven-call interleaving of
/// <c>silo.Services.AddEdict*</c> and Orleans silo-builder calls. The streams
/// and persistence provider extensions live in the provider assembly so the
/// shared kernel doesn't take a transitive Azure dependency.
/// </summary>
public static class EdictSiloBuilderExtensions
{
    /// <summary>
    /// Registers the core Edict mechanisms (outbox engine, dead-letter
    /// projection, sender, idempotency dedup window, claim-check policy seam)
    /// plus the startup wiring validator. Knobs in
    /// <see cref="EdictOptions"/> default to the values previously hardcoded
    /// in mechanism code; the lambda lets a consumer override any subset.
    /// </summary>
    public static ISiloBuilder AddEdict(
        this ISiloBuilder silo,
        Action<EdictOptions>? configure = null)
    {
        silo.Services.AddEdict();
        silo.Services.AddEdictOutbox(configure);
        silo.Services.AddHostedService<EdictWiringValidator>();
        return silo;
    }
}
