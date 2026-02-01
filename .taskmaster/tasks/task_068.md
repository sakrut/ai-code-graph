# Task ID: 68

**Title:** Pipeline slimming: introduce --stages core|full for analyze

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Keep default analysis focused on high-leverage stages; optional stages should be opt-in.

**Details:**

1) Define `core` stages: load/extract/callgraph/metrics/storage (+ minimal required for duplicates).
2) Define `full` stages: core + optional (clusters, token-search/semantic-search if kept).
3) Implement `ai-code-graph analyze ... --stages core|full` with defaults and help text.

**Test Strategy:**

Tests: running with core excludes optional outputs; running with full includes them. CLI help documents stages.
