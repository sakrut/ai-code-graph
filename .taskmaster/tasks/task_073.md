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
