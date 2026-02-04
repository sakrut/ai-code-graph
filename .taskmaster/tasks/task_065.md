# Task ID: 65

**Title:** CLI Output Contract: add --format compact|table|json|csv and make compact the default for agent commands

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Standardize outputs for token economy; keep JSON stable for scripting.

**Details:**

Implement a shared output layer: 
- Add `--format` option to key commands (context/impact/callgraph/hotspots/dead-code/coupling/drift).
- Define `compact` formatting rules (one item per line, bounded lists, stable ids).
- Keep existing table output behind `--format table` for humans.
- Ensure `--format json` remains stable and machine-friendly.

**Test Strategy:**

Add/extend unit tests for formatter(s). Snapshot-test a few commands. Verify help text includes --format.

## Subtasks

### 65.1. Define output contract (compact rules)

**Status:** pending  
**Dependencies:** None  

Write a short spec for compact output and bounds.

**Details:**

Add a `docs/output-contract.md` (or in README/docs) defining: one line per item, bounded lists, stable ids, no ascii tables; define defaults for top/depth/max-items.

### 65.2. Implement shared --format option

**Status:** pending  
**Dependencies:** 65.1  

Introduce shared option helper for --format across commands.

**Details:**

Add shared option builder in CLI (e.g., OutputOptions) and wire `--format` to affected commands without changing default behavior yet.

### 65.3. Add compact formatter for key commands

**Status:** pending  
**Dependencies:** 65.2  

Implement compact output path for context/hotspots/callgraph/impact/dead-code/coupling.

**Details:**

Implement format switch; keep existing table output behind `table`. Ensure compact prints stable identifiers and bounded sections.

### 65.4. Keep JSON stable

**Status:** pending  
**Dependencies:** 65.2  

Ensure JSON schema remains stable and documented.

**Details:**

Add/update serialization DTOs if needed; avoid breaking field names; document versioning strategy.
