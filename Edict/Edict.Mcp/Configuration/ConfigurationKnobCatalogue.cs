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
            new("ReplicationFactor", KnobRequirement.None,
                Footgun: "Assigning ReplicationFactor opts into strict mode: the topic provisioner stops auto-clamping to the available broker count and throws at topic provisioning when the cluster cannot satisfy it, so a one-broker dev cluster will fail. Omit it to keep the auto-clamp."),
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

    // Cross-extension knowledge, hand-authored like the per-knob requirements: which AddEdict*
    // calls bring a stream provider and which bring a persistence provider, plus the one
    // claim-check store that is redundant once Postgres persistence is wired.
    public const string AzureBlobClaimCheckExtension = "AddEdictAzureBlobClaimCheck";
    public const string PostgresPersistenceExtension = "AddEdictPostgresPersistence";

    public static IReadOnlyList<string> StreamProviderExtensions { get; } =
        ["AddEdictAzureStreams", "AddEdictKafkaStreams"];

    public static IReadOnlyList<string> PersistenceProviderExtensions { get; } =
        ["AddEdictAzurePersistence", PostgresPersistenceExtension];
}
