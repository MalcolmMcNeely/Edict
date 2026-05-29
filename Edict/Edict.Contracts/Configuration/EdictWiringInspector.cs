namespace Edict.Contracts.Configuration;

// Pure function over the DI-resolved set of IEdictWiringMarker instances.
// Returns the list of missing-provider descriptions — each names the
// silo.AddEdict* extension the consumer forgot. The host validator turns this
// list (plus the options-validator failures) into one aggregated
// InvalidOperationException at StartAsync.
internal static class EdictWiringInspector
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
