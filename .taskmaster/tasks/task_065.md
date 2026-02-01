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
