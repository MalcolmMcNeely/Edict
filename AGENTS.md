# AGENTS.md

This file is for AI tools opening the Edict repository as a workspace — Codex, IDE coding agents, and similar. It is an index to the human-authored references those tools should ground in before suggesting changes.

## Where the canonical references live

- **[`CONTEXT.md`](CONTEXT.md)** — the domain glossary. One sentence per term, plus the terms each one is meant to be distinct from. Match its vocabulary in any code or doc change.
- **[`docs/usage/`](docs/usage/)** — the consumer surface. [`docs/usage/getting-started.md`](docs/usage/getting-started.md) is the entry point. `concepts/` covers each primitive, `wiring/` covers each substrate, `testing/` covers the in-memory harness.
- **[`docs/adr/`](docs/adr/)** — every load-bearing architectural decision and the rationale behind it. Read the relevant ADR before proposing a change to its area.
- **[`CLAUDE.md`](CLAUDE.md)** — repo conventions enforced at edit time (naming, using directives, exception policy, comment policy).

## The second guardrail: analyzers

`Edict.Analyzers` ([README](Edict/Edict.Analyzers/README.md)) is the compile-time enforcement of the conventions in the docs — the contract rules in the analyzer catalogue (`EDICT001`–`EDICT017`) are the build-breaking enforcement of the surface described under `docs/usage/concepts/`. If a code suggestion would fail an analyzer rule, the docs already describe the right shape.

## Honest framing

This file helps AI tools opening the Edict repository as a workspace. It is not the surface that helps a consumer's AI in a consumer's own repository — that audience is reached through `docs/usage/` page quality and the per-package READMEs published to nuget.org.
