using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;

namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Receiver-side missing-blob dead-letter loop end-to-end against the real
/// Azure provider stack (supersedes the earlier receiver-side slice, lift from
/// <c>Edict.Core.Tests/ClaimCheck/BlobMissingDeadLetterEndToEndTests</c>):
/// when an <see cref="EdictEventEnvelope"/> arrives carrying a
/// <see cref="EdictEventEnvelope.ClaimCheckKey"/> that does not exist in the
/// Azurite blob container, the stream-observer bifurcation stages an
/// <c>InvokeHandler</c> entry; the engine's per-entry retry calls
/// <c>ClaimCheckUnwrap</c> which surfaces
/// <see cref="Azure.RequestFailedException"/> (404) from
/// <see cref="Edict.Azure.ClaimCheck.AzureBlobClaimCheckStore"/>; on
/// <see cref="Contracts.Configuration.EdictOptions.OutboxMaxAttempts"/>
/// exhaustion the standard <c>IDeadLetterPromoter</c> path routes through the
/// BlobMissing failure-kind mapping into the fleet-wide forensic projection
/// (Azure Table). Proves the publisher-side and receiver-side dead-letter
/// flows share the same engine surface against the real transport.
/// </summary>
[Collection(AzureBlobMissingDeadLetterCollection.Name)]
public sealed class AzureBlobMissingDeadLetterEndToEndTests(AzureBlobMissingDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task MissingBlob_ShouldDeadLetterAtMaxAttempts_WithBlobMissingFailureKindAndClaimCheckKey()
    {
        var grainId = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IAzureBlobMissingConsumer>(grainId);

        // Pointer envelope to a key the Azurite blob container does NOT
        // contain — every fetch attempt the engine makes throws
        // Azure.RequestFailedException with status 404.
        var missingKey = $"missing/{Guid.NewGuid():N}";
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: missingKey)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "AzureBlobMissingDeadLetter",
            InnerEventRouteKey = grainId,
        };

        // First delivery stages an InvokeHandler entry and runs the inline
        // drain (attempt #1, fails, bumped). MaxAttempts is 3, so two more
        // reminder ticks exhaust retries — each fires a fresh DrainAsync that
        // re-attempts the gated-then-due entry.
        await consumer.DeliverAsync(envelope);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await consumer.ForceDrainViaReminderAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await consumer.ForceDrainViaReminderAsync();

        var entry = await WaitForBlobMissingRowAsync(missingKey);
        Assert.NotNull(entry);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, entry.FailureKind);
        Assert.Equal(missingKey, entry.ClaimCheckKey);
        Assert.Equal(grainId.ToString(), entry.SourceGrainKey);
        Assert.Contains("AzureBlobMissingConsumer", entry.SourceGrainType);
        Assert.Equal("Azure.RequestFailedException", entry.ExceptionType);
    }

    async Task<EdictDeadLetterEntry?> WaitForBlobMissingRowAsync(string key)
    {
        // EdictDeadLetterProjectionBuilder writes to its literal TableName
        // ("deadletter") — that's the table to inspect. The per-collection
        // DeadLetterTableName backs the operator-facing repository facade in
        // production wiring, but the framework projection itself is not
        // table-name-overridable. The unique ClaimCheckKey isolates this
        // fixture's row from any sibling collection sharing the same Azurite.
        var table = new AzureTableRepository<EdictDeadLetterEntry>(
            fixture.TableServiceClient,
            EdictDeadLetterProjectionBuilder.DeadLetterPartition);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await table.QueryPartitionAsync(
                EdictDeadLetterProjectionBuilder.DeadLetterPartition);
            var match = rows.FirstOrDefault(r => r.ClaimCheckKey == key);
            if (match is not null)
            {
                return match;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return null;
    }
}
