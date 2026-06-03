using Xunit;

namespace Edict.Tests.Conformance.ClaimCheck;

public sealed class InMemoryClaimCheckKeyContractTests(InMemoryClaimCheckStoreFixture fixture)
    : ClaimCheckKeyContractScenarios<InMemoryClaimCheckStoreFixture>(fixture),
        IClassFixture<InMemoryClaimCheckStoreFixture>;
