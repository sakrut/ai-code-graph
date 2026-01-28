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

# Display code structure tree (public methods only by default)
ai-code-graph tree --namespace MyApp

# Include private/internal methods (constructors always excluded)
ai-code-graph tree --include-private

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
├── .claude/commands/cg/        # Claude Code slash commands (12 commands)
│   ├── analyze.md             # /cg:analyze - build code graph
│   ├── context.md             # /cg:context - method context
│   ├── hotspots.md            # /cg:hotspots - complexity hotspots
│   ├── callgraph.md           # /cg:callgraph - call relationships
│   ├── similar.md             # /cg:similar - find similar methods
│   ├── token-search.md        # /cg:token-search - token-based search
│   ├── duplicates.md          # /cg:duplicates - code clones
│   ├── clusters.md            # /cg:clusters - intent clusters
│   ├── tree.md                # /cg:tree - code structure
│   ├── export.md              # /cg:export - export graph data
│   ├── drift.md               # /cg:drift - architectural drift
│   └── churn.md               # /cg:churn - churn hotspots
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

The fastest way to set up Claude Code integration in any .NET project:

```bash
# One-command setup: creates slash commands, CLAUDE.md snippet, and .mcp.json
ai-code-graph setup-claude

# Then analyze your solution
ai-code-graph analyze YourSolution.sln
```

This creates:
- `.claude/commands/cg/*.md` - All 12 slash commands (analyze, context, hotspots, callgraph, similar, token-search, duplicates, clusters, tree, export, drift, churn)
- `.mcp.json` - MCP server configuration exposing all 11 tools for IDE integration
- `CLAUDE.md` snippet - Auto-context instructions for the agent

**Available slash commands after setup:**

| Command | Description |
|---------|-------------|
| `/cg:analyze [solution]` | Analyze a solution and build the code graph |
| `/cg:context <method>` | Get full method context (complexity, callers, callees, cluster, duplicates) |
| `/cg:hotspots` | Show top complexity hotspots as refactoring candidates |
| `/cg:callgraph <method>` | Explore method call relationships (callers and callees) |
| `/cg:similar <method>` | Find methods with similar semantic intent |
| `/cg:token-search <query>` | Token-based code search |
| `/cg:duplicates` | Show detected code clones grouped by type |
| `/cg:clusters` | Show intent clusters (groups of related methods) |
| `/cg:tree` | Display code structure (projects, namespaces, types) |
| `/cg:export` | Export full code graph data as JSON |
| `/cg:drift` | Run drift detection against a saved baseline |
| `/cg:churn` | Show change-frequency x complexity hotspots |

**Auto-context:** The `CLAUDE.md` snippet instructs Claude Code to automatically run `ai-code-graph context` before modifying methods when the graph database exists. This gives the agent architectural awareness without manual intervention.

### MCP Server (for IDEs and Other AI Agents)

The `mcp` subcommand runs a JSON-RPC stdio server implementing the [Model Context Protocol](https://modelcontextprotocol.io/). This lets VS Code, Cursor, Windsurf, and any MCP-compatible client query the code graph.

**Exposed tools:**

| Tool | Parameters | Description |
|------|-----------|-------------|
| `cg_analyze` | `solution`, `save_baseline` (optional) | Analyze a .NET solution and build the code graph |
| `cg_get_context` | `method` (required) | Compact method summary: complexity, callers, callees, cluster, duplicates |
| `cg_get_hotspots` | `top`, `threshold` (optional) | Top N methods by cognitive complexity |
| `cg_get_callgraph` | `method` (required), `depth`, `direction` | Call graph traversal (callers/callees/both) |
| `cg_get_similar` | `method` (required), `top` | Find methods with similar semantic intent |
| `cg_search_code` | `query` (required), `top` (optional) | Natural language code search via embeddings |
| `cg_get_duplicates` | `method`, `top` (optional) | Code clone pairs, optionally filtered to a method |
| `cg_get_clusters` | (none) | List intent clusters with cohesion and members |
| `cg_get_tree` | `namespace`, `type`, `include_private` (optional) | Code structure: projects > namespaces > types > methods (public only by default, constructors always excluded) |
| `cg_export_graph` | `concept` (optional) | Export full graph data (methods, relationships, metrics) |
| `cg_get_drift` | `baseline` (optional) | Detect architectural drift from baseline snapshot |

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
{"tool": "cg_analyze", "arguments": {"solution": "MySolution.sln", "save_baseline": true}}
{"tool": "cg_get_context", "arguments": {"method": "UserService.CreateUser"}}
{"tool": "cg_get_hotspots", "arguments": {"top": 10, "threshold": 15}}
{"tool": "cg_get_callgraph", "arguments": {"method": "Login", "depth": 3, "direction": "both"}}
{"tool": "cg_get_similar", "arguments": {"method": "ValidateInput", "top": 5}}
{"tool": "cg_search_code", "arguments": {"query": "validate user input"}}
{"tool": "cg_get_duplicates", "arguments": {"method": "CheckAuth"}}
{"tool": "cg_get_clusters", "arguments": {}}
{"tool": "cg_get_tree", "arguments": {"namespace": "MyApp.Services", "include_private": false}}
{"tool": "cg_export_graph", "arguments": {"concept": "validation"}}
{"tool": "cg_get_drift", "arguments": {}}
```

### How CG Tools Compare to AI Agent Native Capabilities

For a detailed empirical analysis of where pre-computed code graph tools outperform, match, or underperform an AI agent's built-in exploration workflow (Grep, Glob, Read, Explore agents), see [AI Perspective: Tool Comparison](docs/ai-perspective-tool-comparison.md).

Key findings:
- **Irreplaceable tools** (`coupling`, `hotspots`, `dead-code`): Compute metrics impossible for an AI to derive from text alone
- **Faster tools** (`context`, `tree`, `impact`): Same info the AI could gather, but in 1 call instead of 5-10
- **Inferior tools** (`token-search` with hash embeddings): AI's Grep + reasoning produces better results

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
