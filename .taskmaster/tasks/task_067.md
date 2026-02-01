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
