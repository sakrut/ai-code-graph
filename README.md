# AI Code Graph

A Roslyn-based static analysis tool for .NET codebases that builds semantic code graphs, detects duplicates, computes complexity metrics, and enables natural language code search.

## Features

- **Code Model Extraction** - Parses .NET solutions using Roslyn to extract namespaces, types, methods, and their relationships
- **Call Graph Analysis** - Builds inter-method call graphs including direct calls, interface dispatch, and constructor invocations
- **Cognitive Complexity Metrics** - Computes complexity scores, lines of code, and nesting depth for every method
- **Intent Normalization** - Generates structural signatures and semantic payloads for methods to enable similarity comparison
- **Local Embeddings** - Deterministic feature-hashing embeddings (384-dimensional) with no external API dependencies
- **Duplicate Detection** - Structural (token Jaccard), semantic (vector similarity), and hybrid clone detection
- **Intent Clustering** - DBSCAN-based grouping of methods by semantic intent
- **Natural Language Search** - Query your codebase by intent using cosine similarity on embeddings
- **Drift Detection** - Compare analysis snapshots to detect complexity regressions, new duplicates, and architectural scattering
- **SQLite Storage** - All analysis results persisted to a local SQLite database for querying
- **MCP Server** - Model Context Protocol server for IDE and AI agent integration (VS Code, Cursor, Windsurf)
- **Claude Code Integration** - Slash commands and auto-context for AI-assisted development

## Installation

```bash
# Install as a .NET global tool
dotnet tool install --global AiCodeGraph.Cli

# Or build from source
git clone https://github.com/your-org/ai-code-graph.git
cd ai-code-graph
dotnet build
```

### Requirements

- .NET 8.0 SDK
- MSBuild (included with Visual Studio or the .NET SDK)

## Usage

### Analyze a Solution

```bash
# Analyze a solution and build the code graph database
ai-code-graph analyze path/to/YourSolution.sln

# Save a baseline for drift detection
ai-code-graph analyze path/to/YourSolution.sln --save-baseline
```

### Query Commands

```bash
# Show complexity hotspots (methods above threshold)
ai-code-graph hotspots --top 20 --threshold 10

# Explore call graph for a method
ai-code-graph callgraph --method "MyClass.MyMethod" --depth 3 --direction both

# Display code structure tree
ai-code-graph tree --depth 3

# Find methods similar to a given method
ai-code-graph similar --method "UserService.CreateUser" --top 10

# Search code by natural language
ai-code-graph search "validate user input" --top 10

# Show detected code clones
ai-code-graph duplicates --top 20 --threshold 0.7 --type semantic

# Show intent clusters
ai-code-graph clusters --format json

# Export code graph data
ai-code-graph export --format json --concept "validation"

# Detect drift from a baseline
ai-code-graph drift --vs baseline.db --format detail
```

### Method Context (Single-Call Summary)

```bash
# Get compact context for a method: complexity, callers, callees, cluster, duplicates
ai-code-graph context "MyClass.MyMethod"

# Use a specific database
ai-code-graph context "Validate" --db ./ai-code-graph/graph.db
```

Output example:
```
Method: MyApp.Services.UserService.ValidateUser(string)
File: src/Services/UserService.cs:42
Complexity: CC=12 LOC=35 Nesting=3
Callers (3): AuthController.Login, RegistrationService.Register, AdminService.ResetPassword
Callees (2): UserRepository.FindByEmail, PasswordHasher.Verify
Cluster: "user-validation" (5 members, cohesion: 0.82)
Duplicates: AccountService.CheckCredentials (score: 0.91)
```

### MCP Server Mode

```bash
# Start the MCP server (JSON-RPC over stdio)
ai-code-graph mcp --db ./ai-code-graph/graph.db
```

This launches a Model Context Protocol server exposing the code graph as tools for AI agents and IDEs. See [AI Integration](#ai-integration) for configuration details.

### Output Formats

Most commands support `--format` with options: `table` (default), `json`, or `csv` (where applicable).

### Database Location

By default, the database is stored at `./ai-code-graph/graph.db`. Use `--db <path>` on any command to specify a different location.

## Project Structure

```
ai-code-graph/
├── AiCodeGraph.Cli/           # CLI tool (global tool entry point)
│   ├── Program.cs             # Command definitions and handlers
│   └── Mcp/                   # MCP server (JSON-RPC stdio)
│       └── McpServer.cs       # Protocol handler and tool implementations
├── AiCodeGraph.Core/          # Core analysis library
│   ├── Models/                # Data models (CodeGraph, LoadedWorkspace)
│   ├── CallGraph/             # Call graph builder
│   ├── Metrics/               # Cognitive complexity engine
│   ├── Normalization/         # Intent normalization
│   ├── Embeddings/            # Hash embedding engine and vector index
│   ├── Duplicates/            # Clone detection and intent clustering
│   ├── Drift/                 # Drift detection between snapshots
│   ├── Storage/               # SQLite storage service
│   ├── WorkspaceLoader.cs     # Roslyn MSBuild workspace loader
│   └── CodeModelExtractor.cs  # Syntax/semantic model extraction
├── AiCodeGraph.Tests/         # Unit and integration tests
├── .claude/commands/           # Claude Code slash commands
│   ├── context.md             # /context <method> - method context
│   ├── hotspots.md            # /hotspots - complexity hotspots
│   ├── duplicates.md          # /duplicates - code clones
│   └── drift.md               # /drift - architectural drift
├── tests/fixtures/            # Test fixture solutions
└── .github/workflows/         # CI pipeline
```

## Architecture

The analysis pipeline runs in stages:

1. **Load** - Open .sln/.csproj via MSBuild workspace, get Roslyn compilations
2. **Extract** - Walk syntax trees to build a hierarchical code model (namespaces > types > methods)
3. **Call Graph** - Use semantic model to resolve method invocations across the solution
4. **Metrics** - Compute cognitive complexity by analyzing control flow structures
5. **Normalize** - Generate structural signatures (sorted tokens) and semantic payloads (meaningful identifiers)
6. **Embed** - Produce deterministic 384-dim vectors via feature hashing (SHA256-based)
7. **Detect Clones** - Find duplicates using structural similarity, semantic similarity, and hybrid scoring
8. **Cluster** - Group methods by intent using DBSCAN on embedding vectors
9. **Store** - Persist everything to SQLite for fast querying

## Building

```bash
dotnet build
dotnet test
dotnet pack AiCodeGraph.Cli
```

## Testing

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal
```

The test suite includes:
- Unit tests for each analysis component
- Storage round-trip tests
- Drift detection tests with file-based databases
- Integration tests that exercise the full pipeline (when MSBuild is available)

## AI Integration

AI Code Graph can be used as a context source for AI coding assistants. It provides architectural awareness — complexity, call relationships, duplicates, and clusters — so AI agents make better-informed edits.

### Claude Code

AI Code Graph ships with Claude Code slash commands in `.claude/commands/`. After analyzing your solution, use these commands inside Claude Code:

| Command | Description |
|---------|-------------|
| `/context <method>` | Get full method context (complexity, callers, callees, cluster, duplicates) before editing |
| `/hotspots` | Show top complexity hotspots as refactoring candidates |
| `/duplicates` | Show detected code clones grouped by type |
| `/drift` | Run drift detection against a saved baseline |

**Setup:**

1. Analyze your solution: `ai-code-graph analyze YourSolution.sln`
2. The slash commands read from `./ai-code-graph/graph.db` by default
3. Use `/context MethodName` before modifying any method to understand its role

**Auto-context (CLAUDE.md):** The project's `CLAUDE.md` instructs Claude Code to automatically run `ai-code-graph context` before modifying methods when the graph database exists. This gives the agent architectural awareness without manual intervention.

### MCP Server (for IDEs and Other AI Agents)

The `mcp` subcommand runs a JSON-RPC stdio server implementing the [Model Context Protocol](https://modelcontextprotocol.io/). This lets VS Code, Cursor, Windsurf, and any MCP-compatible client query the code graph.

**Exposed tools:**

| Tool | Parameters | Description |
|------|-----------|-------------|
| `get_context` | `method` (required) | Compact method summary: complexity, callers, callees, cluster, duplicates |
| `get_hotspots` | `top`, `threshold` (optional) | Top N methods by cognitive complexity |
| `search_code` | `query` (required), `top` (optional) | Natural language code search via embeddings |
| `get_duplicates` | `method`, `threshold`, `top` (optional) | Code clone pairs, optionally filtered to a method |

**Configuration for Claude Code / Cursor (.mcp.json):**

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

**Configuration for VS Code (settings.json):**

```json
{
  "mcp.servers": {
    "ai-code-graph": {
      "command": "ai-code-graph",
      "args": ["mcp", "--db", "./ai-code-graph/graph.db"]
    }
  }
}
```

**Usage from any MCP client:**

Once configured, the AI agent can call tools like:
```json
{"tool": "get_context", "arguments": {"method": "UserService.CreateUser"}}
{"tool": "get_hotspots", "arguments": {"top": 10, "threshold": 15}}
{"tool": "search_code", "arguments": {"query": "validate user input"}}
{"tool": "get_duplicates", "arguments": {"threshold": 0.8}}
```

### Standalone CLI for Scripting

All features are available as CLI commands for use in CI pipelines, pre-commit hooks, or custom scripts:

```bash
# Analyze and save baseline in CI
ai-code-graph analyze MySolution.sln --save-baseline

# Fail CI if complexity regresses
ai-code-graph drift --vs baseline.db --format json | jq '.regressions | length'

# Generate hotspot report
ai-code-graph hotspots --top 50 --format csv > hotspots.csv

# Check for new duplicates
ai-code-graph duplicates --threshold 0.9 --format json
```

## License

MIT
