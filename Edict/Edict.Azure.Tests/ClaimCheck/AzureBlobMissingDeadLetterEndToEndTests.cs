using Edict.Azure.Persistence.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.ClaimCheck;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureBlobMissingDeadLetterCollection.Name)]
public sealed class AzureBlobMissingDeadLetterEndToEndTests(AzureBlobMissingDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task MissingBlob_ShouldDeadLetterAtMaxAttempts_WithBlobMissingFailureKindAndSourceEventId()
    {
        var grainId = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IClaimCheckBlobMissingConsumer>(grainId);

        // An EventId the Azurite blob container does NOT contain — every fetch
        // attempt surfaces the store's typed EdictClaimCheckFetchException,
        // which the same classifier arm the Postgres store rides into the
        // Substrate failure bucket.
        var missingEventId = Guid.NewGuid();
        var envelope = new EdictEventEnvelope(inlinePayload: null, eventId: missingEventId)
        {
            OccurredAt = DateTimeOffset.UtcNow,
            InnerEventStreamName = "AzureBlobMissingDeadLetter",
            InnerEventRouteKey = grainId,
        };

        // MaxAttempts is 3; the first delivery runs the inline drain (attempt
        // #1, fails, bumped) and two reminder ticks exhaust retries.
        await consumer.DeliverAsync(envelope);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await consumer.ForceDrainViaReminderAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await consumer.ForceDrainViaReminderAsync();

        var entry = await WaitForBlobMissingRowAsync(missingEventId);
        Assert.NotNull(entry);
        Assert.Equal(EdictDeadLetterFailureKind.BlobMissing, entry.FailureKind);
        Assert.Equal(missingEventId, entry.SourceEventId);
        Assert.Equal(grainId.ToString(), entry.SourceGrainKey);
        Assert.Contains("ClaimCheckBlobMissingConsumer", entry.SourceGrainType);
        Assert.Equal(typeof(EdictClaimCheckFetchException).FullName, entry.ExceptionType);
    }

    async Task<EdictDeadLetterEntry?> WaitForBlobMissingRowAsync(Guid sourceEventId)
    {
        // The projection writes to its literal "deadletter" table — the
        // per-collection DeadLetterTableName only backs the operator-facing
        // repository facade. The unique SourceEventId isolates this row from
        // any sibling collection sharing the same Azurite.
        var table = new AzureTableRepository<EdictDeadLetterEntry>(
            fixture.TableServiceClient,
            EdictDeadLetterTable.Name);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var rows = await table.QueryPartitionAsync(
                EdictDeadLetterTable.Name);
            var match = rows.FirstOrDefault(r => r.SourceEventId == sourceEventId);
            if (match is not null)
            {
                return match;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return null;
    }
}
