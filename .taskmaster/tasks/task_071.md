# Task ID: 71

**Title:** Bench artifacts: gitignore benchmark/ and document local-only benchmarking

**Status:** pending

**Dependencies:** None

**Priority:** low

**Description:** Avoid committing large local db artifacts; keep repo clean.

**Details:**

1) Ensure `benchmark/` is gitignored.
2) Add a short note in docs describing how to run benchmarks locally and where artifacts land.

**Test Strategy:**

Verify `git status` stays clean after creating benchmark db. Verify docs mention this.
