namespace AiCodeGraph.Cli.Commands;

internal static class AgentIntegrationContent
{
    public static string GetCursorRuleContent() => @"---
description: AI Code Graph MCP workflow for C# analysis and edits
alwaysApply: true
---

# AI Code Graph Integration

If `cg_*` MCP tools are available, use them for code-understanding tasks.

Before editing any method, call `cg_get_context` first (when graph data exists) to review complexity, callers, callees, duplicates, and cluster context.

Primary tools:
- `cg_get_context`
- `cg_get_hotspots`
- `cg_get_callgraph`
- `cg_get_impact`
- `cg_query`
- `cg_dead_code`

Secondary tools:
- `cg_token_search`, `cg_semantic_search`, `cg_get_similar`
- `cg_get_duplicates`, `cg_get_clusters`, `cg_export_graph`
- `cg_churn`, `cg_coupling`, `cg_diff`, `cg_get_drift`, `cg_get_tree`, `cg_analyze`

If the database is missing, run `cg_analyze` first to build `./ai-code-graph/graph.db`.
";

    public static string GetSharedSkillContent() => @"---
name: ai-code-graph
description: Guides AI Code Graph workflows for .NET analysis, refactoring, architecture checks, impact analysis, and duplicate detection. Use when users ask about complexity hotspots, blast radius, dead code, coupling, drift, or semantic code lookup.
---

# AI Code Graph Skill

## Quick Start

1. If graph data is missing, run `cg_analyze` on the solution.
2. Before editing a method, run `cg_get_context`.
3. Use method IDs from results for stable follow-up calls.

## Before Editing a Method

Use `cg_get_context` and interpret:
- **Complexity**: If CC > 10, avoid increasing branching/nesting.
- **Callers/Callees**: Estimate blast radius before signature or behavior changes.
- **Duplicates**: Apply the same fix to clone partners when appropriate.
- **Cluster**: Keep changes aligned with the method's intent group.

Then run:
- `cg_get_impact` for transitive caller impact
- `cg_get_callgraph` for direct relationships

## Refactoring Workflows

- **Complexity hotspot review**: `cg_get_hotspots` and prioritize highest CC methods.
- **Dead code cleanup**: `cg_dead_code`, remove or deprecate unreachable methods.
- **Coupling risk scan**: `cg_coupling`, focus on high-instability namespaces/types.
- **Churn risk scan**: `cg_churn`, prioritize high change-frequency x complexity areas.

## Search Workflows

- Use `cg_query` first for deterministic graph-based retrieval.
- Use `cg_token_search` for identifier/token overlap lookups.
- Use `cg_semantic_search` as fallback for natural-language intent matching.
- Use `cg_get_similar` to find candidates for consolidation.

## Architecture Review

- Use `cg_get_drift` to compare against baseline and detect regressions.
- Use `cg_get_clusters` and `cg_get_duplicates` to inspect intent fragmentation.
- Use `cg_export_graph` for structured downstream analysis.
- For layer and dependency policy checks, run CLI commands:
  - `ai-code-graph layers --db ./ai-code-graph/graph.db`
  - `ai-code-graph check-deps --db ./ai-code-graph/graph.db`

## Task-to-Tool Decision Guide

- **""I need context before changing code""** -> `cg_get_context`
- **""What breaks if I change this?""** -> `cg_get_impact` then `cg_get_callgraph`
- **""Where are risky areas?""** -> `cg_get_hotspots`, `cg_churn`, `cg_coupling`
- **""Can we delete this?""** -> `cg_dead_code` and caller checks
- **""Find code related to this idea""** -> `cg_query`, then `cg_token_search`/`cg_semantic_search`
- **""Are we drifting architecturally?""** -> `cg_get_drift`, `cg_get_clusters`, duplicates review
";

    public static string GetCodexOpenAiYamlContent() => @"display_name: AI Code Graph
short_description: .NET graph-aware analysis and refactoring workflows.
default_prompt: Use AI Code Graph MCP tools for context before edits, impact analysis, and architecture checks.
";

    public static string GetAgentsMdSnippet(string dbPath) => $@"
## Auto-Context: Code Graph Integration

When modifying methods in this codebase, run the context command first if `{dbPath}` exists:

```bash
ai-code-graph context ""MethodName"" --db {dbPath}
```

This returns complexity, callers, callees, cluster membership, and duplicates in one call. Use this information to:
- Avoid increasing complexity of already-complex methods (CC > 10)
- Update callers if you change a method's signature or behavior
- Apply the same fix to duplicates when fixing bugs
- Understand which intent cluster a method belongs to before refactoring

For broader workflows, prefer MCP tools:
- `cg_get_context`, `cg_get_impact`, `cg_get_callgraph`
- `cg_get_hotspots`, `cg_dead_code`, `cg_coupling`, `cg_churn`
- `cg_query` first, then `cg_token_search` or `cg_semantic_search` as fallback

To rebuild the graph after significant changes: `ai-code-graph analyze YourSolution.sln`
";
}
