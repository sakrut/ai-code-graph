# Task ID: 69

**Title:** Docs: LLM Quickstart + Minimal Agent Workflow

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Create docs focused on fast context setup for LLMs; reduce token-heavy guidance.

**Details:**

1) Add `docs/LLM-QUICKSTART.md` with the minimal flow: analyze -> context -> impact/callgraph -> hotspots/dead-code.
2) Keep README lean; link to quickstart and deeper docs.
3) Ensure examples use compact output and bounded lists.

**Test Strategy:**

Docs review: quickstart is < 2 pages, actionable, and consistent with CLI behavior.

## Subtasks

### 69.1. Write LLM quickstart

**Status:** pending  
**Dependencies:** None  

Keep it short and aligned with compact-first.

**Details:**

Update `docs/LLM-QUICKSTART.md` to align with `--format compact` defaults and bounded outputs.

### 69.2. Trim README & link docs

**Status:** pending  
**Dependencies:** 69.1  

Keep README as entrypoint and push detail to docs/

**Details:**

Reduce long sections; link to quickstart, output contract, integration docs.
