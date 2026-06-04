using Xunit;

namespace Edict.Pairing.Tests;

[Collection(KafkaPostgresPairingCollection.Name)]
public sealed class KafkaPostgresCompositionBootTests(KafkaPostgresPairingFixture fixture)
    : CompositionBootScenarios<KafkaPostgresPairingFixture>(fixture);
