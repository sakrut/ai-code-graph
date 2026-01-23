# Claude Code Integration PRD

## Overview
Add Claude Code integration to the ai-code-graph CLI tool, enabling automatic code context retrieval before edits, slash commands for manual queries, and an MCP server mode for IDE integrations.

## Features

### 1. Context Subcommand
Add a `context` CLI subcommand that returns a compact, combined summary for a given method - complexity, callers, callees, cluster membership, and duplicates in a single call. This minimizes round-trips and context usage.

**Usage:** `ai-code-graph context <method-pattern> [--db path]`

**Output format (plain text, compact):**
```
Method: Namespace.Type.Method(params)
File: path/to/file.cs:42
Complexity: CC=12 LOC=35 Nesting=3
Callers (3): CallerA, CallerB, CallerC
Callees (2): CalleeX, CalleeY
Cluster: "cluster-label" (N members, cohesion: 0.XX)
Duplicates: MethodA (score: 0.95), MethodB (score: 0.82)
```

If no duplicates or cluster, omit those lines. If method not found, show "Method not found" with suggestions.

### 2. Slash Commands (.claude/commands/)
Create `.claude/commands/` directory with markdown files that define reusable slash commands:

- `context.md` - Get full context for a method before editing
- `hotspots.md` - Show top complexity hotspots
- `duplicates.md` - Show code clones
- `drift.md` - Run drift detection against baseline

Each command should include clear instructions for Claude on how to invoke the CLI and interpret results.

### 3. CLAUDE.md Auto-Context Instructions
Update the project's CLAUDE.md to instruct Claude Code to automatically run `ai-code-graph context <method>` before modifying any method with complexity > 5 or that has callers. This provides architectural awareness without manual intervention.

### 4. MCP Server Mode
Add `ai-code-graph mcp` subcommand that runs a JSON-RPC stdio MCP server exposing 4 focused tools:

- `get_context` - Combined method context (same as CLI context command)
- `get_hotspots` - Top N complexity hotspots
- `search_code` - Natural language code search
- `get_duplicates` - Clone pairs for a method or globally

The MCP server should:
- Use stdin/stdout JSON-RPC (MCP protocol)
- Open the pre-built graph.db on startup
- Return compact text responses (not verbose JSON) to save tokens
- Handle the standard MCP lifecycle (initialize, list tools, call tool)

## Constraints
- Keep responses compact - every token counts in Claude's context
- Single CLI binary, no new projects needed
- The graph.db must already exist (pre-built via `analyze`)
- MCP server should be lightweight - no background threads, just request/response
