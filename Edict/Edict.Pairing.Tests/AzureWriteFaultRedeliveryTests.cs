using Xunit;

namespace Edict.Pairing.Tests;

[Collection(AzurePairingCollection.Name)]
public sealed class AzureWriteFaultRedeliveryTests(AzurePairingFixture fixture)
    : WriteFaultRedeliveryScenarios<AzurePairingFixture>(fixture);
