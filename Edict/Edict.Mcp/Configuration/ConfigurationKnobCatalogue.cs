namespace Edict.Mcp.Configuration;

// Hand-maintained, mirroring SiloWiringScanner.KnownExtensions: Edict.Mcp references no
// provider assemblies, so the knob names, their requirement, and the AddEdict* that brings
// each options class into play are recorded here by hand. The knob-name column is held to the
// real options surface by ConfigurationKnobCatalogueDriftTests; the requirement column is
// hand-authored knowledge that type metadata cannot recover.
static class ConfigurationKnobCatalogue
{
    public static IReadOnlyList<ConfigurationOptionsEntry> Entries { get; } =
    [
        new("EdictOptions", "AddEdict",
        [
            new("IdempotencyWindowSize", KnobRequirement.None),
            new("CorrelationWindowSize", KnobRequirement.None),
            new("ProjectionReadTimeout", KnobRequirement.None),
            new("OutboxBaseDelay", KnobRequirement.None),
            new("OutboxMaxDelay", KnobRequirement.None),
            new("OutboxMaxAttempts", KnobRequirement.None),
            new("OutboxJitterFraction", KnobRequirement.None),
            new("OutboxDrainReminderPeriod", KnobRequirement.None),
        ]),
        new("EdictSagaOptions", "AddEdict",
        [
            new("DefaultTimeout", KnobRequirement.None),
        ]),
        new("EdictCommandHandlerScheduleOptions", "AddEdict",
        [
            new("DefaultTimeout", KnobRequirement.None),
        ]),
        new("EdictAzureStreamsOptions", "AddEdictAzureStreams",
        [
            new("StreamProviderName", KnobRequirement.None),
            new("ClaimCheckThresholdBytes", KnobRequirement.None),
            new("QueuePollingPeriod", KnobRequirement.None),
            new("NumQueues", KnobRequirement.None),
            new("QueueServiceClient", KnobRequirement.ConfirmExternally),
        ]),
        new("EdictAzurePersistenceOptions", "AddEdictAzurePersistence",
        [
            new("GrainStateContainerName", KnobRequirement.None),
            new("TableServiceClient", KnobRequirement.None),
            new("BlobServiceClient", KnobRequirement.None),
        ]),
        new("EdictAzureBlobClaimCheckOptions", "AddEdictAzureBlobClaimCheck",
        [
            new("ContainerName", KnobRequirement.None),
            new("BlobServiceClient", KnobRequirement.None),
        ]),
        new("EdictKafkaStreamsOptions", "AddEdictKafkaStreams",
        [
            new("StreamProviderName", KnobRequirement.None),
            new("BootstrapServers", KnobRequirement.Required),
            new("ConsumerGroupId", KnobRequirement.None),
            new("PartitionCount", KnobRequirement.None),
            new("PartitionCountByStream", KnobRequirement.None),
            new("ReplicationFactor", KnobRequirement.None),
            new("MinInSyncReplicas", KnobRequirement.None),
            new("Compression", KnobRequirement.None),
            new("MessageTimeout", KnobRequirement.None),
            new("AutoOffsetReset", KnobRequirement.None),
            new("ProducerConfigOverrides", KnobRequirement.None),
            new("ConsumerConfigOverrides", KnobRequirement.None),
        ]),
        new("EdictPostgresPersistenceOptions", "AddEdictPostgresPersistence",
        [
            new("ConnectionString", KnobRequirement.Required),
            new("Invariant", KnobRequirement.None),
            new("GrainStorageProviderName", KnobRequirement.None),
            new("ClaimCheckTableName", KnobRequirement.None),
            new("BootstrapSchema", KnobRequirement.None),
            new("MaxPoolSize", KnobRequirement.None),
            new("MinPoolSize", KnobRequirement.None),
            new("StorageRetryCount", KnobRequirement.None),
            new("StorageRetryBaseDelay", KnobRequirement.None),
        ]),
    ];
}
