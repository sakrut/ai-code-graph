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

## Subtasks

### 68.1. Define stages (core vs full)

**Status:** pending  
**Dependencies:** None  

Decide which stages belong to core.

**Details:**

Document stages mapping to pipeline steps; decide defaults and CLI help text.

### 68.2. Implement --stages flag in analyze

**Status:** pending  
**Dependencies:** 68.1  

Allow selecting pipeline subsets.

**Details:**

Wire flag to pipeline runner; ensure optional stages are skipped when core.

### 68.3. Make optional features opt-in

**Status:** pending  
**Dependencies:** 68.2  

Move weaker features behind full stage or flag.

**Details:**

Token-search/semantic-search/clustering only if enabled; ensure commands gracefully explain missing stage.
