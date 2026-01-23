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

### Output Formats

Most commands support `--format` with options: `table` (default), `json`, or `csv` (where applicable).

### Database Location

By default, the database is stored at `./ai-code-graph/graph.db`. Use `--db <path>` on any command to specify a different location.

## Project Structure

```
ai-code-graph/
├── AiCodeGraph.Cli/           # CLI tool (global tool entry point)
│   └── Program.cs             # Command definitions and handlers
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

## License

MIT
