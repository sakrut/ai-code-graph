# Task ID: 67

**Title:** DB Metadata + Staleness Detection (db-info/status command)

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** Make it obvious when graph.db is stale, and provide a cheap staleness check.

**Details:**

1) Add an AnalysisMetadata table (or equivalent) storing: analyzedAt, solutionPath, toolVersion, gitCommit (if available).
2) Implement `ai-code-graph db-info` (or `status`) that prints compact metadata and a staleness hint.
3) Staleness heuristic: compare git HEAD commit and/or last modified times of *.csproj/*.cs vs analyzedAt.

**Test Strategy:**

Manual: run analyze, then db-info; modify a file; db-info should warn. Tests for metadata round-trip and heuristic behavior.

## Subtasks

### 67.1. Add AnalysisMetadata table

**Status:** pending  
**Dependencies:** None  

Persist analyzedAt, toolVersion, solutionPath, gitCommit.

**Details:**

Extend SQLite schema and storage layer to write metadata on analyze.

### 67.2. Implement db-info/status command

**Status:** pending  
**Dependencies:** 67.1  

Expose metadata and staleness hint in compact form.

**Details:**

Add CLI command that reads metadata and prints: analyzedAt, solution, tool version, git commit; plus stale/not stale hint.

### 67.3. Implement staleness heuristic

**Status:** pending  
**Dependencies:** 67.2  

Detect likely stale db cheaply.

**Details:**

Compare git HEAD commit (if repo) and/or last modified times of relevant files vs analyzedAt. Keep best-effort and explain uncertainty.
