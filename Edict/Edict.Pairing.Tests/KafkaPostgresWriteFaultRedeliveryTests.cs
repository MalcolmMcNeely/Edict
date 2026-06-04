using Xunit;

namespace Edict.Pairing.Tests;

[Collection(KafkaPostgresPairingCollection.Name)]
public sealed class KafkaPostgresWriteFaultRedeliveryTests(KafkaPostgresPairingFixture fixture)
    : WriteFaultRedeliveryScenarios<KafkaPostgresPairingFixture>(fixture);
