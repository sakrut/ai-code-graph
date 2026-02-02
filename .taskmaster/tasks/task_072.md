# Task ID: 72

**Title:** Refactor CLI entrypoint to support shared options (format, db, compact) consistently

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Reduce drift between commands and make future changes cheaper.

**Details:**

1) Introduce shared option builders / helpers for: --db, --format, --top, --threshold, --include-private, etc.
2) Apply consistently across commands.
3) Ensure help output stays readable and options are grouped logically.

**Test Strategy:**

Unit tests for option parsing. Run `ai-code-graph --help` and spot-check command helps.

## Subtasks

### 72.1. Shared option helpers

**Status:** pending  
**Dependencies:** None  

Centralize common options to reduce drift.

**Details:**

Introduce helpers for: --db, --format, --top, --threshold, --depth, --include-private; refactor a few commands.

### 72.2. Help text organization

**Status:** pending  
**Dependencies:** 72.1  

Improve readability of CLI help.

**Details:**

Group options, ensure defaults documented, keep concise.
