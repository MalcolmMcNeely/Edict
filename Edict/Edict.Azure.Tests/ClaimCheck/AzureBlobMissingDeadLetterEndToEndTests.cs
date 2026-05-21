using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Core.DeadLetter;

namespace Edict.Azure.Tests.ClaimCheck;

[Collection(AzureBlobMissingDeadLetterCollection.Name)]
public sealed class AzureBlobMissingDeadLetterEndToEndTests(AzureBlobMissingDeadLetterClusterFixture fixture)
{
    [Fact]
    public async Task MissingBlob_ShouldDeadLetterAtMaxAttempts_WithBlobMissingFailureKindAndClaimCheckKey()
    {
        var grainId = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IAzureBlobMissingConsumer>(grainId);

        // Key that the Azurite blob container does NOT contain — every
        // fetch attempt throws RequestFailedException with status 404.
        var missingKey = $"missing/{Guid.NewGuid():N}";
        var envelope = new EdictEventEnvelope(inlinePayload: null, claimCheckKey: missingKey)
        {
            EventId = Guid.NewGuid(),
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
        // The projection writes to its literal "deadletter" table — the
        // per-collection DeadLetterTableName only backs the operator-facing
        // repository facade. The unique ClaimCheckKey isolates this row from
        // any sibling collection sharing the same Azurite.
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
