using Edict.Contracts.Commands;

using Xunit;

namespace Edict.Architecture.Tests;

// Every public top-level type in a framework assembly is on a hand-maintained
// allow-list. A new public type therefore fails CI until the maintainer either
// flips it to internal or extends the allow-list — forcing the ADR-0017
// brand-rule conversation at PR review rather than relying on review of any
// individual PR.
public class PublicSurfaceAllowListTests
{
    [Fact]
    public void EdictContracts_PublicTypesMatchAllowList()
    {
        var contractsAssembly = typeof(EdictCommand).Assembly;
        var actual = contractsAssembly
            .GetExportedTypes()
            .Where(t => !t.IsNested)
            .Select(t => t.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var unexpected = actual.Where(name => !EdictContractsAllowList.Contains(name)).ToList();
        var missing = EdictContractsAllowList.Where(name => !actual.Contains(name)).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            BuildDriftMessage(unexpected, missing));
    }

    static readonly HashSet<string> EdictContractsAllowList = new(StringComparer.Ordinal)
    {
        "Edict.Contracts.ClaimCheck.EdictEnvelopeOverflowException",
        "Edict.Contracts.Commands.EdictCommand",
        "Edict.Contracts.Commands.EdictCommandResult",
        "Edict.Contracts.Commands.EdictRejectionReason",
        "Edict.Contracts.Commands.EdictRouteKeyAttribute",
        "Edict.Contracts.Configuration.EdictOptions",
        "Edict.Contracts.Configuration.EdictPersistenceProviderMarker",
        "Edict.Contracts.Configuration.EdictStreamsProviderMarker",
        "Edict.Contracts.Configuration.IEdictWiringMarker",
        "Edict.Contracts.DeadLetter.EdictDeadLetterEntry",
        "Edict.Contracts.DeadLetter.EdictDeadLetterFailureKind",
        "Edict.Contracts.DeadLetter.EdictDeadLetterRaised",
        "Edict.Contracts.DeadLetter.IEdictDeadLetterRepository",
        "Edict.Contracts.EdictUnit",
        "Edict.Contracts.Events.EdictEvent",
        "Edict.Contracts.Events.EdictEventEnvelope",
        "Edict.Contracts.Events.EdictStreamAttribute",
        "Edict.Contracts.Persistence.IEdictPersistedState",
        "Edict.Contracts.Routing.EdictEventStreamAccessor",
        "Edict.Contracts.Routing.EdictEventStreamsAttribute",
        "Edict.Contracts.Routing.EdictEventTagWritersAttribute",
        "Edict.Contracts.Routing.EdictRoutesAttribute",
        "Edict.Contracts.Sending.IEdictSender",
        "Edict.Contracts.TableStorage.IEdictTableRepository`1",
        "Edict.Contracts.TableStorage.IEdictTableWriteStore`1",
        "Edict.Contracts.Telemetry.EdictTelemeterizedAttribute",
    };

    static string BuildDriftMessage(IReadOnlyList<string> unexpected, IReadOnlyList<string> missing)
    {
        var sections = new List<string>();
        if (unexpected.Count > 0)
        {
            sections.Add("Public types not on the allow-list (either flip to internal or extend the list):\n  - "
                + string.Join("\n  - ", unexpected));
        }
        if (missing.Count > 0)
        {
            sections.Add("Allow-list entries that no longer exist in the assembly (remove from the list):\n  - "
                + string.Join("\n  - ", missing));
        }
        return string.Join("\n\n", sections);
    }
}
