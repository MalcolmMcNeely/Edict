# MCP tools

Seven tools ship with `edict-mcp`. You don't call them directly — you ask the agent (Claude Code, Cursor, etc.) for what you want, one of the five skills fires, and the skill body tells the agent which tool to reach for. This page shows the kind of prompt that triggers each tool and what comes back. The end-to-end walkthrough lives in [skills.md](skills.md).

## driftStatus

Every structured response carries a `driftStatus` flag with one of four values:

- `clean` — the `edict-mcp` tool version matches the `Edict.*` library the solution references.
- `drifted` — they differ. The response may describe a different framework version than the consumer is coding against.
- `inconsistent-library-versions` — the solution references more than one `Edict.*` version.
- `no-edict-references` — the solution references no `Edict.*` library at all.

Anything other than `clean` or `no-edict-references` is worth investigating — see [troubleshooting.md](troubleshooting.md).

## edict_list_handlers

> "Add a SubmitOrder command to the Orders aggregate."

`edict-authoring` fires, and before writing any code the agent calls `edict_list_handlers` to see what's already there. You get back the handler inventory: every consumer subclass in your solution (Command Handler, Command Validator, Event Handler, Saga, Projection Builder, List Projection Builder), the contracts each one is bound to, the `[EdictRouteKey]` property on each contract, and the source file. Each Saga also reports its effective `[EdictSagaTimeout]` cap: a duration literal, `unbounded`, or `default` when it inherits the silo-wide `EdictSagaOptions.DefaultTimeout`, so the agent can answer "which sagas have caps" without reading source. The agent uses it to spot that `OrderCommandHandler` already exists and the new `HandleAsync` overload should extend that partial, not start a parallel handler.

## edict_list_route_keys

> "Add a SubmitOrder command to the Orders aggregate."

Called alongside `edict_list_handlers` before code is written. You get the same inventory regrouped: Commands listed by their handler classes, Events by their subscriber classes, plus a `collisions` array. A non-empty `collisions` means a Command is bound to two different handlers — the one thing in the response that's an actionable defect rather than informational.

## edict_describe_silo_wiring

> "Set up the Azure Blob claim-check store."

`edict-silo-wiring` fires on any `Program.cs` edit. The agent calls `edict_describe_silo_wiring` before suggesting any wiring change. You get two arrays — `wired` (the `AddEdict*` extensions already in the chain) and `missing` (known extensions an agent should consider) — each with the declaring assembly and a one-line purpose. For the claim-check request `AddEdictAzureBlobClaimCheck` shows up in `missing` and the agent recommends exactly that extension instead of guessing your silo's substrate from grep.

## edict_check_configuration

> "Is my silo wired correctly enough to start?"

`edict-silo-wiring` fires after the `AddEdict*` chain is in place, and `edict-diagnostics` fires when a silo throws at boot. The agent calls `edict_check_configuration` to get a verdict on the *configuration inside* the wiring, not just which extensions are present. It reads `Program.cs`, works out which option knobs you have set inside each `AddEdict*` call (resolving set-versus-not-set even when the value comes from a variable or `IConfiguration`, not a literal), and returns findings in three categories: required-but-unset knobs (an empty Kafka `BootstrapServers`, an unset Postgres `ConnectionString`, and a soft reminder to confirm an Azure `QueueServiceClient` is set on the options or registered in DI), known footguns (an explicitly-assigned `ReplicationFactor` opting the topic provisioner into strict mode, which throws on a one-broker dev cluster), and incomplete extension combinations (a stream provider wired with no persistence provider, or `AddEdictAzureBlobClaimCheck` wired alongside Postgres persistence). Each finding carries a severity, a category, the options type, the knob name, a message, and a source location.

The cross-extension findings reuse the same wired-extension view as `edict_describe_silo_wiring`, so the two tools never disagree about what is wired. The verdict is **best-effort and advisory**: it resolves only whether a knob is set, never whether the value is sensible, and it defers to `EdictWiringValidator` — which runs at host start with live DI and aggregates every problem into one `EdictWiringException` — as ground truth. A brand-new project is the degenerate case where almost everything is missing, so the same tool that diagnoses an established silo walks a fresh one off the ground.

## edict_describe_glossary_term

> "Does OrderSubmittedEvent need its own stream?"

`edict-contracts` fires. The agent looks up `"Domain Stream"` and gets back the raw `CONTEXT.md` entry — definition, an `_Avoid_` list of anti-patterns, any cross-references. The `_Avoid_` list names per-event-type streams as the anti-pattern, so the agent puts the new Event on the existing `Orders` stream.

`"Saga"`, `"saga"`, and `"EdictSaga"` all resolve to the same entry — case is ignored and the `Edict` prefix is optional. Unknown terms return `Glossary term '<term>' not found in CONTEXT.md.`.

## edict_lookup_adr

> "Why is there no RedriveAsync on dead-letter rows?"

`edict-contracts` calls this for wire-format "why" questions; `edict-diagnostics` calls it for runtime-behaviour questions. You get the raw markdown body of the matching ADR — title, decision narrative, considered options, consequences. Query by number (`"28"` or `"0028"`) or fuzzy title substring (`"dead letter"`, `"claim check"`).

Unmatched queries return `ADR matching query '<query>' not found.`. ADR bodies are embedded at build time, so a sibling response's `driftStatus: "drifted"` is the signal that the ADR text may pre- or post-date your framework version.

## edict_describe_mcp_state

> "Why is edict_list_handlers returning empty when my handlers obviously exist?"

`edict-diagnostics` fires when an MCP result looks off. You get back the loaded solution path, the indexed-handler count, the full `Edict.*` version report (tool version vs each library reference, plus the `driftStatus`), and the list of registered tools. The single field that ends most "why am I seeing nothing?" investigations is `loadedSolutionPath` — if it points at the wrong workspace, the `--solution` override in `.mcp.json` is the fix.

This tool never errors. A missing or unreadable solution surfaces here as a populated state object.

## See also

- Agentic tooling — [Setup](setup.md), [Skills](skills.md), [Troubleshooting](troubleshooting.md).
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
