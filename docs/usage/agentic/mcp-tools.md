# MCP tools

Six tools ship with `edict-mcp`. Five are called by the load-bearing skills (`edict-authoring`, `edict-contracts`, `edict-silo-wiring`, `edict-diagnostics`); the sixth, `edict_describe_mcp_state`, is the diagnostics-tier surface an agent reaches for when results look off. This page is the per-tool reference — name, when called, input arg shape, a canonical return-shape snippet, and the failure modes worth knowing. The full response snapshots live in [skills.md](skills.md) — this page is for surface lookup.

Every Tier-1 tool that emits structured JSON includes a top-level `driftStatus` field whose value comes from the Edict tool-vs-library version report: `clean` (versions match), `drifted` (the `edict-mcp` tool version differs from the library version referenced by the loaded solution), `inconsistent-library-versions` (the solution references more than one `Edict.*` library version), or `no-edict-references` (the loaded solution does not reference any `Edict.*` library at all). Treat any value other than `clean` or `no-edict-references` as a signal that the rest of the response may be describing a different framework version than the consumer is coding against; [troubleshooting.md](troubleshooting.md) covers the resolution.

## edict_list_handlers

- **Name** — `edict_list_handlers`.
- **When called** — `edict-authoring` invokes this load-bearing, before writing code, on the addition of any new Command, Event, Command Handler, Event Handler, Saga, Projection Builder, or Table Projection Builder. The returned inventory tells the agent whether an existing handler already covers the Command (extend the partial) or whether the new role is unique to the solution (write a new subclass).
- **Input arg shape** — none. Schema:
  ```json
  { "type": "object", "properties": {} }
  ```
- **Return shape** — `{ "handlers": [...], "driftStatus": "..." }`, one entry per consumer-defined subclass of `EdictCommandHandler` / `EdictEventHandler` / `EdictSaga` / `EdictProjectionBuilder` / `EdictTableProjectionBuilder` in the loaded solution. Each entry carries `role`, `boundContracts` (with the `[EdictRouteKey]` property name on each contract), `declaringAssembly`, and a `sourceLocation`. Canonical fragment:
  ```json
  {
    "handlers": [
      {
        "declaringTypeName": "FixtureLibrary.Orders.OrderCommandHandler",
        "role": "commandHandler",
        "boundContracts": [
          {
            "fullTypeName": "FixtureLibrary.Orders.CancelOrderCommand",
            "routeKeyPropertyName": "OrderId"
          },
          {
            "fullTypeName": "FixtureLibrary.Orders.PlaceOrderCommand",
            "routeKeyPropertyName": "OrderId"
          }
        ],
        "declaringAssembly": "FixtureLibrary",
        "sourceLocation": {
          "filePath": "FixtureLibrary/Orders/OrderCommandHandler.Cancel.cs",
          "line": 3,
          "column": 1
        }
      }
    ],
    "driftStatus": "no-edict-references"
  }
  ```
- **Failure modes worth knowing** — `driftStatus: "drifted"` or `"inconsistent-library-versions"` means the inventory was scanned against a different `Edict.*` version than the one shipped inside `edict-mcp`'s embedded ADRs and conventions, so a recommendation derived from the result may cite a removed type or miss a recently added one. `handlers: []` is a legitimate response for a solution that has no consumer subclasses yet (a fresh project) — distinguish it from a misaligned workspace by checking `edict_describe_mcp_state.loadedSolutionPath` first.

## edict_list_route_keys

- **Name** — `edict_list_route_keys`.
- **When called** — `edict-authoring` invokes this load-bearing alongside `edict_list_handlers`, before writing code, on the addition of any new Command or Event. The Commands view exposes route-key collisions a `[EdictRouteKey]` decision is about to cause; the Events view exposes the subscriber fan-out before a new Projection Builder or Saga is added against an existing Event.
- **Input arg shape** — none. Schema:
  ```json
  { "type": "object", "properties": {} }
  ```
- **Return shape** — `{ "commands": [...], "events": [...], "collisions": [...], "driftStatus": "..." }`. Commands are grouped by their handler classes; Events by their subscriber classes. `collisions` is the set of Commands bound to more than one handler — empty in a healthy solution. Canonical fragment:
  ```json
  {
    "commands": [
      {
        "commandType": "FixtureLibrary.Orders.PlaceOrderCommand",
        "routeKeyProperty": "OrderId",
        "handlers": [
          "FixtureLibrary.Orders.OrderCommandHandler"
        ]
      }
    ],
    "events": [
      {
        "eventType": "FixtureLibrary.Orders.OrderPlaced",
        "routeKeyProperty": "OrderId",
        "subscribers": [
          "FixtureLibrary.Activity.OrderActivityProjection"
        ]
      }
    ],
    "collisions": [],
    "driftStatus": "no-edict-references"
  }
  ```
- **Failure modes worth knowing** — a non-empty `collisions` array is the only thing in the response that is itself an actionable defect; everything else is descriptive. The same `driftStatus` caveats as `edict_list_handlers` apply, since this view is derived from the same handler inventory. An Event with `subscribers: []` is a legitimate "no consumer wired yet" state and not an error.

## edict_describe_silo_wiring

- **Name** — `edict_describe_silo_wiring`.
- **When called** — `edict-silo-wiring` invokes this load-bearing, before any change to `Program.cs` or any other silo-wiring file. The `wired` array tells the agent which substrate the silo is already on so a recommendation is grounded in the actual chain instead of guessed from grep; the `missing` array names the known extensions an agent should consider — for example `AddEdictAzureBlobClaimCheck` when the consumer asks for a Claim Check setup against the Azure streaming pairing.
- **Input arg shape** — none. Schema:
  ```json
  { "type": "object", "properties": {} }
  ```
- **Return shape** — `{ "programSourceLocation": {...}, "wired": [...], "missing": [...], "driftStatus": "..." }`. Each entry in `wired` and `missing` carries `extensionName`, `declaringAssembly`, and a one-line `purpose`. Canonical fragment:
  ```json
  {
    "programSourceLocation": {
      "filePath": "FixtureLibrary/Program.cs",
      "line": 1,
      "column": 1
    },
    "wired": [
      {
        "extensionName": "AddEdict",
        "declaringAssembly": "FixtureLibrary",
        "purpose": "Registers the Edict framework: handler discovery, outbox, telemetry."
      }
    ],
    "missing": [
      {
        "extensionName": "AddEdictAzureBlobClaimCheck",
        "declaringAssembly": "Edict.Azure.Streaming",
        "purpose": "Enables the Azure Blob claim-check store for large event payloads."
      }
    ],
    "driftStatus": "no-edict-references"
  }
  ```
- **Failure modes worth knowing** — the `missing` catalogue is hand-maintained inside `edict-mcp` because the server stays substrate-neutral; a `driftStatus` other than `clean` or `no-edict-references` means the catalogue may not match the version of the `AddEdict*` surfaces the loaded solution is coding against. If `Program.cs` cannot be located (a non-standard silo entry point), `programSourceLocation` is the field to inspect first.

## edict_describe_glossary_term

- **Name** — `edict_describe_glossary_term`.
- **When called** — `edict-contracts` invokes this for contract-term lookups (`Domain Stream`, `Route Key`, `Telemeterized`) when authoring or modifying a Command or Event contract. `edict-authoring` invokes it for role-name lookups ("what is a Saga?", "what does Projection Builder mean here?") when a consumer's request leaves the role ambiguous.
- **Input arg shape** — one required string. Schema (verbatim from the registry):
  ```json
  {
    "type": "object",
    "properties": {
      "term": {
        "type": "string",
        "description": "Glossary term to look up. Case-insensitive; the optional 'Edict' prefix is elidable so 'Saga', 'saga', and 'EdictSaga' all resolve to the same entry."
      }
    },
    "required": ["term"]
  }
  ```
- **Return shape** — the raw markdown body of the matching `CONTEXT.md` entry: definition, an `_Avoid_` list of anti-patterns, and any inline cross-references. The body is returned as `text` content, not JSON. Canonical fragment:
  ```markdown
  **Domain Stream**:
  A named Orleans stream that carries every event type for one domain, declared once via `[Stream("Name")]` on the concrete event type.
  _Avoid_: per-event-type streams; inferring the stream name from the CLR namespace; a publisher and subscriber naming the stream independently.
  ```
- **Failure modes worth knowing** — an unknown term returns the literal string `Glossary term '<term>' not found in CONTEXT.md.`. A missing or non-string `term` argument returns `Missing required argument 'term' (string).`. The glossary content is embedded in the `edict-mcp` tool at build time, so `driftStatus` on the other tools' responses is the indicator of whether the glossary the agent is reading matches the framework version the consumer is coding against — this tool itself does not carry a drift field.

## edict_lookup_adr

- **Name** — `edict_lookup_adr`.
- **When called** — `edict-contracts` invokes this load-bearing for the wire-format "why" questions (MessagePack-first, `[Alias]`, no `[Union]`). `edict-diagnostics` invokes it load-bearing for the "why does dead-letter / Outbox / claim-check behave this way?" questions when investigating a runtime failure.
- **Input arg shape** — one required string. Schema (verbatim from the registry):
  ```json
  {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "ADR number (e.g. '28' or '0028') or a fuzzy substring of the ADR title."
      }
    },
    "required": ["query"]
  }
  ```
- **Return shape** — the raw markdown body of the matching ADR (title heading, decision narrative, considered options, consequences). Returned as `text` content, not JSON. Canonical fragment:
  ```markdown
  # Dead letter (forensic-only, table-projection-backed)

  Dead-lettering is **observability, not back-pressure**: when an Outbox entry exhausts `MaxAttempts`, the engine — in the same one grain-state write — removes the failed entry from the Outbox slice and appends a new `PublishEvent` entry carrying an `EdictDeadLetterRaised` event...
  ```
- **Failure modes worth knowing** — an unmatched query returns the literal string `ADR matching query '<query>' not found.`. A missing or non-string `query` argument returns `Missing required argument 'query' (string: ADR number like '28' or '0028', or a fuzzy title substring).`. ADR bodies are embedded in the `edict-mcp` tool at build time; a `driftStatus: "drifted"` on a sibling response means the ADR text the agent is reading may pre- or post-date the framework version the consumer is coding against. Resolution path: pin the `edict-mcp` manifest version to the same `Edict.*` library version the consumer's solution references.

## edict_describe_mcp_state

- **Name** — `edict_describe_mcp_state`.
- **When called** — `edict-diagnostics` invokes this load-bearing when MCP results look off (an empty handler list when handlers obviously exist, a `IEdictDeadLetterRepository` query that returns empty when rows obviously exist). The single field that ends most "why am I seeing nothing?" investigations is `loadedSolutionPath`: a mismatch with the consumer's actual workspace is the documented explanation. It is also the only tool an agent should reach for to read the version-drift report directly rather than inferring drift from a sibling tool's `driftStatus`.
- **Input arg shape** — none. Schema:
  ```json
  { "type": "object", "properties": {} }
  ```
- **Return shape** — `{ "loadedSolutionPath": "...", "indexedHandlerCount": N, "edictVersionReport": {...}, "registeredTools": [...] }`. The `edictVersionReport` exposes the tool version, each `Edict.*` library reference the solution carries, and the same `driftStatus` classification the sibling tools surface. `registeredTools` enumerates the tools the server has registered with their descriptions. Canonical fragment:
  ```json
  {
    "loadedSolutionPath": "{REPO_ROOT}/Edict/Edict.Mcp.Tests/Fixtures/TracerBulletFixture/TracerBulletFixture.slnx",
    "indexedHandlerCount": 5,
    "edictVersionReport": {
      "toolVersion": "{TOOL_VERSION}",
      "references": [],
      "isDrifted": false,
      "hasNoEdictReferences": true,
      "hasInconsistentLibraryVersions": false,
      "driftStatus": "no-edict-references"
    },
    "registeredTools": [
      {
        "name": "edict_describe_mcp_state",
        "description": "Self-diagnostic. Reports the loaded solution path, indexed-handler count, the Edict tool-vs-library version report, and the list of MCP tools the server has registered."
      }
    ]
  }
  ```
- **Failure modes worth knowing** — `loadedSolutionPath` pointing at the wrong workspace is the most common cause of "the other tools returned nothing useful"; the documented fix is the `--solution` override in `.mcp.json`. `edictVersionReport.driftStatus` carries the same four values described at the top of this page; `drifted` and `inconsistent-library-versions` are the two values that warrant action — see [troubleshooting.md](troubleshooting.md). This tool itself has no failure path that does not return a value: it does not load the workspace lazily, so a missing or unreadable solution surfaces here as a populated state object rather than an error.

## See also

- Agentic tooling — [Setup](setup.md), [Skills](skills.md), [Troubleshooting](troubleshooting.md).
- ADRs — [0044 — Agentic tooling](../../adr/0044-agentic-tooling.md).
