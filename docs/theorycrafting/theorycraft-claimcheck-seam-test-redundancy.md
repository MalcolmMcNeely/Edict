# Theorycraft: claim-check seam-test redundancy after the conformance battery

**Date:** 2026-06-03
**Origin:** Flagged on close of [#287](https://github.com/MalcolmMcNeely/Edict/issues/287) (commit `ec44364`). The new cross-substrate `ClaimCheckKeyContractScenarios` battery now covers round-trip + missing uniformly, overlapping per-provider seam tests that predate it. This doc frames the prune-or-keep decision for a later grill — it is **not** a decision.

**Framing (to grill against):** the maintainer dislikes redundant coverage and reference-convenience placement (memory `feedback-no-reference-convenience-placement`). The battery was added as *additive* proof (the issue said so), so the overlap was left untouched on purpose. The question is whether the per-provider round-trip/missing halves now earn their keep, or are dead weight the battery subsumes.

---

## The overlap, precisely

| Test | File | Now covered by battery? | Keep regardless? |
|---|---|---|---|
| `PutAsync_ShouldStoreBodyThatRoundTripsViaGetAsync` | `Edict/Edict.Azure.Tests/ClaimCheck/AzureBlobClaimCheckStoreTests.cs:24` | **Yes** — `PutThenGetByEventId_ShouldReturnIdenticalBytes` (Azure binding) | — |
| `GetAsync_ShouldThrowFetchException_WhenBodyMissing` | `…/AzureBlobClaimCheckStoreTests.cs:37` | **Yes** — `GetUnknownEventId_ShouldThrowFetchException_ClassifyingToSubstrate` (Azure binding) | — |
| `PutAsync_ShouldThrow_WhenSameEventIdWrittenTwice` | `…/AzureBlobClaimCheckStoreTests.cs:52` | **No** — battery has no re-PUT scenario by design (AC3) | **Yes** — provider-specific `overwrite:false` collision proof |
| `AzureBlobClaimCheckStore_ShouldNotExposeDeleteApi` | `…/AzureBlobClaimCheckStoreTests.cs:66` | No | **Yes** — append-only structural guard |
| `ClaimCheckStore_ShouldRoundTripBytes` | `Edict/Edict.Postgres.Tests/PostgresProviderUnitTests.cs:61` | **Yes** — Postgres binding | — |
| `ClaimCheckStore_ShouldExposeNoDeleteApi` | `Edict/Edict.Postgres.Tests/PostgresProviderUnitTests.cs:42` | No | **Yes** — append-only structural guard |

Notes:
- Postgres has **no** per-provider *missing* test — the battery's Postgres binding is net-new missing coverage.
- Kafka×blob had **no** per-provider seam test at all — battery binding is net-new.
- So the strict redundancy is: **Azure round-trip + missing, and Postgres round-trip** (3 test methods).

## What the battery does NOT replace

The duplicate-PUT collision (`overwrite:false` / Postgres PK) and the no-`DeleteAsync` structural guards are provider-specific and deliberately out of the conformance battery (the battery asserts only the cross-substrate key contract). Any prune must keep these.

## The argument each way

**Prune the 3 redundant halves.** Honest single-source-of-truth: the contract is asserted once, uniformly, for every substrate. Matches the anti-redundancy stance. A future reader isn't left wondering why Azure asserts round-trip in two places.

**Keep them.** Two non-obvious differences make them not pure duplicates:
1. **Fixture cost / path.** The per-provider tests use a *bare-client* fixture (`AzureBlobClaimCheckStoreTests` news up its own `BlobServiceClient` + container; `PostgresProviderUnitTests` news up the store over a raw `NpgsqlDataSource`). The battery bindings ride the **cluster** fixtures (`AzureClaimCheckClusterFixture` etc.), which stand up a full silo. The bare-client test fails faster and more locally when the *store alone* is broken, with no Orleans noise in the failure.
2. **Co-location.** The round-trip/missing halves sit in the same file as the structural guards that must stay, so pruning splits a previously cohesive provider-store unit-test file.

## Open questions for the grill

1. Is the bare-client-vs-cluster distinction a *real* signal-value difference, or rationalisation? (If the store breaks, do both fail anyway — making the bare-client one redundant in practice?)
2. If pruned, do the remaining structural-only files (`ShouldExposeNoDeleteApi` + duplicate-PUT) still read as coherent, or should those guards move/merge somewhere?
3. Should the battery itself absorb the **duplicate-PUT collision** as a cross-substrate scenario? (It was excluded as "no re-PUT" — but the collision-on-duplicate is a *different* assertion from the re-drain idempotency path the AC ruled out. Worth distinguishing: re-drain never re-PUTs (unreachable, correctly excluded) vs. a same-id second PUT throwing loudly (a real provider invariant, currently only per-provider).)
4. Is "uniform single source" worth losing the fast-local bare-store failure mode, or could the battery gain a no-cluster in-memory-style binding that exercises a bare store per provider?

## Relevant context

- Battery design + accessibility-bridge rationale: [#287](https://github.com/MalcolmMcNeely/Edict/issues/287) close comment and commit `ec44364`.
- Seam single-identity model: PRD [#285](https://github.com/MalcolmMcNeely/Edict/issues/285), ADR-0053, memory `claim-check-key-eventid-grill`.
- Anti-redundancy / placement rule: memory `feedback-no-reference-convenience-placement`; CLAUDE.md "Conventions".
- Suggested skill for the grill: `grill-me` (or `grill-with-docs` if it should also touch the testing skill / ADR-0016 placement guidance).
