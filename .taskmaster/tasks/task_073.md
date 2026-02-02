# Task ID: 73

**Title:** Regression test suite: output snapshots for key commands in compact and json modes

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Prevent token-regressions and accidental output bloat.

**Details:**

1) Add snapshot tests (golden files) for: context, hotspots, callgraph, impact, dead-code, coupling.
2) Test both `--format compact` and `--format json`.
3) Add CI step that fails if snapshots change without explicit update.

**Test Strategy:**

CI green; snapshot update workflow documented.

## Subtasks

### 73.1. Golden snapshot tests

**Status:** pending  
**Dependencies:** None  

Add snapshot tests for compact + json outputs.

**Details:**

Create golden files and a harness; cover context/hotspots/callgraph/impact/dead-code/coupling.

### 73.2. Document snapshot update workflow

**Status:** pending  
**Dependencies:** 73.1  

Make it easy to update intentionally.

**Details:**

Add a short doc for regenerating snapshots and reviewing diffs.
