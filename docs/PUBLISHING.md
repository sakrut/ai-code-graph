# Publishing & Setup Guide

## Publishing to NuGet

AI Code Graph is packaged as a .NET global tool. Publishing makes it installable via:

```bash
dotnet tool install --global AiCodeGraph.Cli
```

### Automated Release (Recommended)

Push a version tag to main to trigger the release pipeline:

```bash
# Update version in AiCodeGraph.Cli/AiCodeGraph.Cli.csproj if needed
# Then tag and push:
git tag v0.1.0
git push origin v0.1.0
```

The `.github/workflows/release.yml` pipeline will:
1. Run tests on all platforms
2. Pack the global tool
3. Push to NuGet.org automatically

**Prerequisites:**
- Add `NUGET_API_KEY` as a repository secret in GitHub Settings > Secrets > Actions
- Get your API key from https://www.nuget.org/account/apikeys (scope: push packages for `AiCodeGraph.Cli`)

### Manual Release

```bash
# Pack
dotnet pack AiCodeGraph.Cli --configuration Release

# Push to NuGet.org
dotnet nuget push AiCodeGraph.Cli/nupkg/AiCodeGraph.Cli.*.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

NuGet indexing takes ~15 minutes after push.

### Version Bumping

Update the `<Version>` in `AiCodeGraph.Cli/AiCodeGraph.Cli.csproj`:

```xml
<Version>0.2.0</Version>
```

Follow semver: breaking changes = major, new features = minor, fixes = patch.

---

## User Setup Guide

### Install the Tool

```bash
dotnet tool install --global AiCodeGraph.Cli
```

### Quick Start (Any .NET Project)

```bash
cd your-dotnet-project

# 1. Set up Claude Code integration (slash commands, MCP config, CLAUDE.md)
ai-code-graph setup-claude

# 2. Analyze your solution
ai-code-graph analyze YourSolution.sln
```

That's it. Claude Code now has full architectural awareness of your codebase.

### What `setup-claude` Creates

| File | Purpose |
|------|---------|
| `.claude/commands/analyze.md` | `/analyze` - build code graph |
| `.claude/commands/context.md` | `/context` - method context before editing |
| `.claude/commands/hotspots.md` | `/hotspots` - complexity hotspots |
| `.claude/commands/callgraph.md` | `/callgraph` - call relationships |
| `.claude/commands/similar.md` | `/similar` - find similar methods |
| `.claude/commands/search.md` | `/search` - natural language search |
| `.claude/commands/duplicates.md` | `/duplicates` - code clones |
| `.claude/commands/clusters.md` | `/clusters` - intent clusters |
| `.claude/commands/tree.md` | `/tree` - code structure |
| `.claude/commands/export.md` | `/export` - export graph data |
| `.claude/commands/drift.md` | `/drift` - architectural drift |
| `.mcp.json` | MCP server config (11 tools for IDE integration) |
| `CLAUDE.md` (appended) | Auto-context instructions for the agent |

### Using with Claude Code

After setup, these slash commands are available in Claude Code sessions:

```
/analyze MySolution.sln   # Build/rebuild the code graph
/context ValidateUser     # Complexity, callers, callees, cluster, duplicates
/hotspots                 # Top methods by cognitive complexity
/callgraph Login          # Call tree for a method
/similar CreateUser       # Find semantically similar methods
/search "validate input"  # Natural language code search
/duplicates               # Detected code clones
/clusters                 # Intent clusters
/tree                     # Code structure tree
/export                   # Export graph data as JSON
/drift                    # Changes since baseline
```

Claude Code will also automatically query method context before editing (via the CLAUDE.md instructions).

### Using with MCP-Compatible IDEs

The `.mcp.json` created by `setup-claude` works with:
- **Claude Code** - auto-detected
- **VS Code** (Copilot) - copy config to `.vscode/settings.json` under `mcp.servers`
- **Cursor** - auto-detected from `.mcp.json`
- **Windsurf** - auto-detected from `.mcp.json`

The MCP server exposes 11 tools:

| Tool | Parameters | Description |
|------|-----------|-------------|
| `analyze` | `solution`, `save_baseline` | Build/rebuild the code graph |
| `get_context` | `method` (required) | Method summary with all relationships |
| `get_hotspots` | `top`, `threshold` | Complexity hotspots |
| `get_callgraph` | `method`, `depth`, `direction` | Call graph traversal |
| `get_similar` | `method`, `top` | Find similar methods |
| `search_code` | `query`, `top` | Natural language code search |
| `get_duplicates` | `method`, `top` | Code clone pairs |
| `get_clusters` | (none) | Intent clusters |
| `get_tree` | `namespace`, `type` | Code structure |
| `export_graph` | `concept` | Export graph data as JSON |
| `get_drift` | `baseline` | Architectural drift detection |

### Using Standalone (CI / Scripts)

```bash
# Save a baseline for drift detection
ai-code-graph analyze MySolution.sln --save-baseline

# Check for complexity regressions in CI
ai-code-graph drift --vs baseline.db --format json

# Generate hotspot report
ai-code-graph hotspots --top 50 --format csv > hotspots.csv

# Find duplicates above threshold
ai-code-graph duplicates --threshold 0.9 --format json
```

### Rebuilding the Graph

After significant code changes, rebuild:

```bash
ai-code-graph analyze YourSolution.sln
```

The database at `./ai-code-graph/graph.db` is overwritten with fresh analysis.
