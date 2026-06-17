using Xunit;

namespace Edict.Pairing.Tests;

[Collection(KafkaPostgresPairingCollection.Name)]
public sealed class KafkaPostgresTenantRoundTripTests(KafkaPostgresPairingFixture fixture)
    : TenantRoundTripScenarios<KafkaPostgresPairingFixture>(fixture);
