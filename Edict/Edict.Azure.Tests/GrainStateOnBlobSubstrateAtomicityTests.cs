using System.Text;

using Azure.Storage.Blobs.Models;

using Orleans;
using Orleans.Runtime;

namespace Edict.Azure.Tests;

/// <summary>
/// ADR 0025 conformance: prove that Orleans's <c>AzureBlobGrainStorage</c>,
/// the substrate every Edict grain's state rides on after the sample wiring
/// swap, is single-blob ETag atomic — a successful write lands one writer's
/// full picture, never a field-level mix of several writers'. Pounding the
/// same grain key with 16 concurrent writers and reading the persisted blob
/// directly through the Azurite client converts ADR 0025's "Orleans says so"
/// into "Edict's CI proves so". The grain has no Edict framework type in its
/// surface so the substrate is the only thing under test.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class GrainStateOnBlobSubstrateAtomicityTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task GrainStateOnBlobSubstrate_ShouldPersistExactlyOneWritersVersion_WhenWritersConflictOnSameKey()
    {
        const int WriterCount = 16;
        const int MarkersPerWriter = 32;

        var grainKey = Guid.NewGuid();
        var grain = fixture.Cluster.GrainFactory
            .GetGrain<IBlobSubstrateAtomicityGrain>(grainKey);

        // Each writer's tag is a UTF-8 string starting with a unique prefix —
        // visible in the persisted blob bytes regardless of Orleans's
        // serializer's internal Guid encoding, so the atomicity check is
        // decoupled from how the substrate serializes a Guid.
        var writers = Enumerable.Range(0, WriterCount)
            .Select(i => $"writer-{i}-{Guid.NewGuid():N}")
            .ToArray();

        var tasks = writers
            .Select(tag => Task.Run(() => grain.WriteAsync(tag, MarkersPerWriter)))
            .ToArray();
        await Task.WhenAll(tasks);

        var container = fixture.BlobServiceClient.GetBlobContainerClient("edict-state");
        var blobs = new List<BlobItem>();
        await foreach (var blob in container.GetBlobsAsync())
        {
            blobs.Add(blob);
        }

        // The grain key is a fresh GUID; Orleans encodes it into the blob
        // name. Try both common Guid string formats so the test does not
        // couple to a specific Orleans naming convention.
        var keyD = grainKey.ToString("D");
        var keyN = grainKey.ToString("N");
        var matching = blobs
            .Where(b =>
                b.Name.Contains(keyD, StringComparison.OrdinalIgnoreCase) ||
                b.Name.Contains(keyN, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count != 1)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected exactly one matching blob for grain key {keyD} / {keyN}; " +
                $"saw {matching.Count}. All blobs in container 'edict-state': " +
                string.Join(", ", blobs.Select(b => b.Name)));
        }

        var blobBytes = (await container
            .GetBlobClient(matching[0].Name)
            .DownloadContentAsync())
            .Value.Content.ToArray();

        // Substrate-atomicity invariant: exactly one writer's tag appears
        // in the persisted blob. If the write were torn, two or more tags
        // would be mixed in the same document.
        var presentWriters = writers
            .Where(tag => Contains(blobBytes, Encoding.UTF8.GetBytes(tag)))
            .ToArray();

        Assert.Single(presentWriters);

        // The single present writer's marker pattern must appear at the full
        // multiplicity it wrote (WriterTag field + MarkersPerWriter list
        // entries). A lower count would mean partial / interleaved bytes.
        var winnerBytes = Encoding.UTF8.GetBytes(presentWriters[0]);
        var occurrences = CountOccurrences(blobBytes, winnerBytes);
        Assert.True(
            occurrences >= MarkersPerWriter,
            $"Winning writer {presentWriters[0]} should appear at least " +
            $"{MarkersPerWriter} times in the blob; saw {occurrences}.");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }
        return IndexOf(haystack, needle, 0) >= 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        var last = haystack.Length - needle.Length;
        for (var i = start; i <= last; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    private static int CountOccurrences(byte[] haystack, byte[] needle)
    {
        var count = 0;
        var i = 0;
        while (true)
        {
            var hit = IndexOf(haystack, needle, i);
            if (hit < 0)
            {
                return count;
            }
            count++;
            i = hit + needle.Length;
        }
    }
}

public interface IBlobSubstrateAtomicityGrain : IGrainWithGuidKey
{
    Task WriteAsync(string writerTag, int markerCount);
}

[GenerateSerializer]
public sealed class BlobSubstrateAtomicityState
{
    [Id(0)]
    public string WriterTag { get; set; } = "";

    [Id(1)]
    public List<string> Markers { get; set; } = [];
}

public sealed class BlobSubstrateAtomicityGrain(
    [PersistentState("state", "edict-state")]
    IPersistentState<BlobSubstrateAtomicityState> state)
    : Grain, IBlobSubstrateAtomicityGrain
{
    public async Task WriteAsync(string writerTag, int markerCount)
    {
        state.State.WriterTag = writerTag;
        state.State.Markers = Enumerable.Repeat(writerTag, markerCount).ToList();
        await state.WriteStateAsync();
    }
}
