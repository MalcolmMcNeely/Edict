using Xunit;

namespace Edict.Pairing.Tests;

[Collection(AzurePairingCollection.Name)]
public sealed class AzureCompositionBootTests(AzurePairingFixture fixture)
    : CompositionBootScenarios<AzurePairingFixture>(fixture);
