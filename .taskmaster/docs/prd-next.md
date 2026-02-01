# AI Code Graph — Next Milestone PRD (Token-Efficient Code Navigation for LLMs)

## 0) Intent
Refocus AI Code Graph into a **high-signal / low-token** code navigation layer for LLM agents working on .NET repos.

Primary value proposition: **fast, semantically correct context reconstruction** (call graph + complexity + coupling + dead-code) with minimal output.

## 1) Problem
LLMs are slow and token-expensive when they have to discover:
- where code lives (structure),
- what depends on what (call graph + interface dispatch),
- what is risky to change (impact, coupling),
- what is worth refactoring (hotspots),
- what can be deleted safely (dead-code).

Pure grep/read exploration is:
- O(N) tool calls,
- noisy (false positives),
- not semantically aware (interface dispatch, overrides),
- very expensive in tokens.

## 2) Goals (next milestone)
### G1 — Token economy as default
- Provide `--compact` output across the CLI.
- Make compact mode the default for agent-facing commands (`context`, `impact`, `callgraph`, `hotspots`, `dead-code`, `coupling`).

### G2 — Make the “agent flow” effortless
- A single recommended workflow: analyze → context → impact/callgraph.
- Clear docs for agent integration.

### G3 — Keep only high-leverage features in the default pipeline
- Make weaker features optional (hash-only semantic search / token-search).
- Ensure the default stages maximize signal-per-token.

### G4 — Reliability & staleness detection
- Make it obvious when the db is out-of-date.
- Provide a cheap staleness check (commit hash + file timestamps).

## 3) Non-goals (this milestone)
- Multi-repo / monorepo federation.
- Runtime tracing.
- Cloud-only dependency (keep local-first).
- Perfect semantic search quality (optional stage).

## 4) Scope / Deliverables
### D1 — Output contract: compact-first
- Add `--format compact|table|json|csv` where applicable.
- `compact` rules:
  - one line per row item
  - stable identifiers
  - no ASCII tables
  - bounded lists (top N + “...”) with `--top` / `--max-items`

### D2 — Method identity & selection
- Consistent, stable `MethodId` in outputs.
- Allow selecting a method by:
  - exact fully qualified signature,
  - substring match,
  - `--id <MethodId>`.

### D3 — Staleness metadata
- Store analysis metadata in DB:
  - analyzedAt
  - solution path
  - git commit hash (if available)
  - tool version
- Add `ai-code-graph status` (or `ai-code-graph db-info`) that prints:
  - whether db looks stale
  - what solution it was built from
  - last analyzed timestamp

### D4 — Feature gating / pipeline slimming
- Introduce a simple stage selector:
  - `ai-code-graph analyze ... --stages core` (default)
  - `--stages full` (includes optional stages)
- `core` stages should include: extract, callgraph, metrics, (optional) hash-embed only if required by duplicates/clusters.
- Optional stages: token-search/semantic-search improvements.

### D5 — Documentation refresh
- Add a “LLM quickstart” doc focused on minimal context.
- Keep README short; move deep docs to `docs/`.

## 5) User Stories
1. As an LLM agent, I can run `context` and get a small, deterministic summary for a method before editing.
2. As an engineer, I can quickly identify the riskiest modules (coupling/instability) before introducing changes.
3. As an engineer, I can identify top complexity hotspots without reading the entire repo.
4. As an engineer, I can spot likely dead code safely.
5. As an LLM agent, I can detect staleness and avoid using outdated graphs.

## 6) Acceptance Criteria
- `context` output in compact mode is <= ~25 lines for typical methods.
- `hotspots`, `dead-code`, `coupling` have bounded outputs by default.
- `db-info/status` clearly indicates when db is likely stale.
- CLI help documents compact mode and recommended flows.
- No regression in existing command names/options without a compatibility note.

## 7) Risks
- Refactoring CLI output may break scripts → mitigate with `--format json` stability.
- Staleness heuristics can produce false positives → provide “best-effort” and clear messaging.

## 8) Notes
This PRD intentionally optimizes for **signal-per-token**. If a feature does not improve signal-per-token, it should be optional.
