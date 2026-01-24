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
| `.claude/commands/context.md` | `/context <method>` - method context before editing |
| `.claude/commands/hotspots.md` | `/hotspots` - complexity hotspots |
| `.claude/commands/duplicates.md` | `/duplicates` - code clone detection |
| `.claude/commands/drift.md` | `/drift` - architectural drift |
| `.mcp.json` | MCP server config for IDE integration |
| `CLAUDE.md` (appended) | Auto-context instructions for the agent |

### Using with Claude Code

After setup, these slash commands are available in Claude Code sessions:

```
/context ValidateUser     # Shows complexity, callers, callees, cluster, duplicates
/hotspots                 # Top methods by cognitive complexity
/duplicates               # Detected code clones
/drift                    # Changes since baseline
```

Claude Code will also automatically query method context before editing (via the CLAUDE.md instructions).

### Using with MCP-Compatible IDEs

The `.mcp.json` created by `setup-claude` works with:
- **Claude Code** - auto-detected
- **VS Code** (Copilot) - copy config to `.vscode/settings.json` under `mcp.servers`
- **Cursor** - auto-detected from `.mcp.json`
- **Windsurf** - auto-detected from `.mcp.json`

The MCP server exposes these tools:

| Tool | Parameters | Description |
|------|-----------|-------------|
| `get_context` | `method` (required) | Method summary with all relationships |
| `get_hotspots` | `top`, `threshold` | Complexity hotspots |
| `search_code` | `query`, `top` | Natural language code search |
| `get_duplicates` | `method`, `threshold`, `top` | Code clone pairs |

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
