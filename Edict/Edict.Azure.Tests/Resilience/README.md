# Resilience tests

Transport-fault scenarios against Azurite, introduced by issue #96 under the
ADR-0029 integration-realism rework. The category proves the framework
survives substrate downtime: messages are not lost, sagas resume from durable
state, and grain activation converges once Azurite is reachable again.

## Why this collection owns its own Azurite container

The rest of `Edict.Azure.Tests` shares a single Azurite container through
`AzuriteAssemblyHost` and relies on per-fixture Guid-prefixed resource names
for cross-collection isolation. That model breaks the moment any test
modifies Azurite's lifecycle: a `docker pause` issued by the resilience suite
would hang every other collection's blob/queue/table call until the unpause,
turning unrelated tests into spurious failures and timeouts.

The resilience collection therefore opts out of the shared-Azurite parallelism
model. `ResilienceClusterFixture.InitializeAsync` starts its own
`AzuriteContainer` and disposes it at fixture teardown; the collection is
declared with `DisableParallelization = true` so its three scenarios execute
sequentially against that container, never interleaving with each other's
substrate state. Other collections continue to run in parallel — they touch a
different Azurite — so the cost is one extra container per assembly run.

## Why `docker pause` and not `RestartAsync`

Testcontainers .NET 3.10 does not surface `PauseAsync` on `IContainer`, so the
fixture talks to Docker.DotNet directly. `docker pause` was chosen over
`docker stop` + `docker start` because the latter re-binds the container to a
new ephemeral host port. The silo's already-configured Azure Queue / Blob
clients hold the old port and would never reconnect, masking what we want to
prove (that Edict's retry-and-converge behaviour survives a substrate outage)
behind a host-wiring artefact. `docker pause` preserves the port and the
SIGSTOP-suspended Azurite process resumes from exactly where it was on
unpause — the closest analogue to the "transient transport disruption"
scenario in the issue.

`RestartAzuriteAsync` is kept on the fixture as a deliberate stop/start
(filesystem-preserving) helper for tests that want to prove convergence
across a true container restart. Such a test must use a fresh consumer
grain key after the restart, since pre-restart grain activations hold stale
Azure client connections to the old host port.

## Scenarios

1. `AzuriteStoppedMidPublishTests` — event published, Azurite paused, then
   unpaused. Asserts exactly-once delivery.
2. `AzuriteRestartedMidSagaTests` — saga trigger event published, Azurite
   paused long enough to outlast the queue's visibility timeout, then
   unpaused. Asserts the saga records `Progress` once and the tracker
   command lands once.
3. `AzuriteUnavailableAtStartupTests` — Azurite paused before the first
   substrate-touching operation. Asserts the publish hangs while paused, and
   completes (with exactly-once handler invocation) after unpause.

## Out of scope

No Toxiproxy / network fault proxy — deliberately deferred until the
conformance-harness era when a second provider justifies the sidecar
container. No silo-kill mid-`HandleAsync` test (that scenario is a separate
slice on top of issue #96's parent).
