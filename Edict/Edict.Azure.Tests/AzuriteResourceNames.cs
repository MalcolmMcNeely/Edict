namespace Edict.Azure.Tests;

/// <summary>
/// A Guid-prefixed bundle of Azurite resource names for a single test or
/// fixture instance. Cross-collection isolation is the whole point: two
/// collections sharing the assembly-scoped Azurite (ADR 0029) must not collide
/// on table, queue, or blob container names. Tests that create ad-hoc
/// resources (per-test tables or claim-check containers) construct one of
/// these via <c>fixture.NewResourceNames()</c>; fixtures use one of their own
/// at startup for the silo-wired containers.
/// </summary>
public sealed record AzuriteResourceNames(
    string TableName,
    string QueueName,
    string BlobContainerName)
{
    public static AzuriteResourceNames Generate(string prefix = "edict")
    {
        var token = Guid.NewGuid().ToString("N");
        return new AzuriteResourceNames(
            TableName: $"{prefix}tbl{token}",
            QueueName: $"{prefix}-q-{token}",
            BlobContainerName: $"{prefix}-blob-{token}");
    }
}
