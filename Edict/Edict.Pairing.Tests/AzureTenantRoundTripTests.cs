using Xunit;

namespace Edict.Pairing.Tests;

[Collection(AzurePairingCollection.Name)]
public sealed class AzureTenantRoundTripTests(AzurePairingFixture fixture)
    : TenantRoundTripScenarios<AzurePairingFixture>(fixture);
