namespace Edict.Contracts.Configuration;

/// <summary>
/// Pure function over the DI-resolved set of <see cref="IEdictWiringMarker"/>
/// instances. Returns the list of missing-provider descriptions —
/// each names the <c>silo.AddEdict*</c> extension the consumer forgot. The
/// host validator turns this list (plus the options-validator failures) into
/// one aggregated <see cref="InvalidOperationException"/> at
/// <c>StartAsync</c>.
/// </summary>
public static class EdictWiringInspector
{
    public static IReadOnlyList<string> Inspect(IEnumerable<IEdictWiringMarker> markers)
    {
        var missing = new List<string>();
        var seen = new HashSet<Type>();
        foreach (var marker in markers)
        {
            seen.Add(marker.GetType());
        }

        if (!seen.Contains(typeof(EdictStreamsProviderMarker)))
        {
            missing.Add("Missing streams provider: call silo.AddEdictAzureStreams(...) (or another streams provider) at silo startup.");
        }

        if (!seen.Contains(typeof(EdictPersistenceProviderMarker)))
        {
            missing.Add("Missing persistence provider: call silo.AddEdictAzurePersistence(...) (or another persistence provider) at silo startup.");
        }

        return missing;
    }
}
