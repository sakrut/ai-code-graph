# AI Code Graph

A Roslyn-based static analysis tool for .NET codebases. Builds semantic code graphs with call relationships, complexity metrics, and duplicate detection.

**For LLM/AI agents:** See [LLM-QUICKSTART.md](docs/LLM-QUICKSTART.md) for minimal-token workflows.

## Features

- **Call Graph** - Method relationships including interface dispatch
- **Complexity Metrics** - Cognitive complexity, LOC, nesting depth
- **Duplicate Detection** - Structural and semantic clone detection
- **Coupling Analysis** - Afferent/efferent coupling, instability
- **Dead Code Detection** - Methods with no callers
- **Staleness Detection** - Know when to re-analyze
- **MCP Server** - IDE and AI agent integration

## Installation

```bash
dotnet tool install --global AiCodeGraph.Cli
```

Requires: .NET 8.0 SDK

## Quick Start

```bash
# Analyze a solution
ai-code-graph analyze YourSolution.sln

# Check database status
ai-code-graph status

# Get method context before editing
ai-code-graph context "ValidateUser"

# Find complexity hotspots
ai-code-graph hotspots --top 10

# Check blast radius
ai-code-graph impact --id "<method-id>"
```

## Commands

| Command | Description |
|---------|-------------|
| `analyze` | Build the code graph database |
| `status` | Show DB info and staleness check |
| `context` | Method summary (callers, callees, metrics) |
| `callgraph` | Explore call relationships |
| `impact` | Transitive caller analysis |
| `hotspots` | High-complexity methods |
| `dead-code` | Uncalled methods |
| `coupling` | Coupling/instability metrics |
| `duplicates` | Code clone detection |
| `tree` | Code structure display |
| `drift` | Compare with baseline |

All commands support `--format compact|table|json` (default: compact for agent commands).

## AI Integration

### Claude Code

```bash
# One-command setup
ai-code-graph setup-claude
```

Creates slash commands (`/cg:context`, `/cg:hotspots`, etc.) and MCP server config.

### Cursor

```bash
# One-command setup
ai-code-graph setup-cursor
```

Creates `.cursor/mcp.json`, `.cursor/rules/ai-code-graph.mdc`, and `.cursor/skills/ai-code-graph/SKILL.md`.

### Codex

```bash
# One-command setup
ai-code-graph setup-codex
```

Creates `.codex/skills/ai-code-graph/SKILL.md`, `.codex/skills/ai-code-graph/agents/openai.yaml`, `.mcp.json`, and `AGENTS.md` integration guidance.

### MCP Server

```bash
ai-code-graph mcp --db ./ai-code-graph/graph.db
```

Configure in `.mcp.json`:
```json
{
  "mcpServers": {
    "ai-code-graph": {
      "type": "stdio",
      "command": "ai-code-graph",
      "args": ["mcp", "--db", "./ai-code-graph/graph.db"]
    }
  }
}
```

## Documentation

- [LLM Quickstart](docs/LLM-QUICKSTART.md) - Minimal-token agent workflow
- [Output Contract](docs/output-contract.md) - Format specifications

## Building from Source

```bash
git clone https://github.com/your-org/ai-code-graph.git
cd ai-code-graph
dotnet build
dotnet test
```

## License

MIT
