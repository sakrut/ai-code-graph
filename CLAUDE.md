# Claude Code Instructions

## Project Overview

AI Code Graph is a .NET 8.0 Roslyn-based static analysis tool packaged as a global CLI tool. It analyzes .NET solutions to build semantic code graphs stored in SQLite.

## Build & Test

```bash
dotnet build                          # Build all projects
dotnet test                           # Run all tests (303 tests)
dotnet test --filter "ClassName"      # Run specific test class
dotnet pack AiCodeGraph.Cli           # Package as global tool
```

## Project Structure

- `AiCodeGraph.Cli/` - CLI entry point using System.CommandLine 2.0.2. All commands defined in `Program.cs`.
- `AiCodeGraph.Core/` - Core library with all analysis logic. Subdivided by concern:
  - `Models/` - CodeGraph models, LoadedWorkspace record
  - `CallGraph/` - CallGraphBuilder using Roslyn semantic model
  - `Metrics/` - MetricsEngine for cognitive complexity
  - `Normalization/` - IntentNormalizer for structural signatures and semantic payloads
  - `Embeddings/` - HashEmbeddingEngine (384-dim feature hashing), VectorIndex (SIMD cosine similarity)
  - `Duplicates/` - StructuralCloneDetector, SemanticCloneDetector, HybridScorer, IntentClusterer (DBSCAN)
  - `Drift/` - DriftDetector comparing two SQLite databases
  - `Storage/` - StorageService (SQLite), SchemaDefinition
- `AiCodeGraph.Tests/` - xUnit tests
- `tests/fixtures/TestSolution/` - Multi-project fixture for integration tests

## Key Patterns

- **System.CommandLine**: Uses `SetAction()` on commands, `Option<T>` for parameters. Stable 2.0.2 API.
- **Storage**: `StorageService` wraps Microsoft.Data.Sqlite. Call `InitializeAsync()` to create tables, or `OpenAsync()` for existing DBs.
- **Embeddings**: Deterministic hash-based (no ML models). `HashEmbeddingEngine` implements `IEmbeddingEngine`.
- **Clone Detection**: Three-layer approach - structural (Jaccard on tokens), semantic (kNN on vectors), hybrid (weighted merge).
- **MSBuild Locator**: Called once in `WorkspaceLoader` constructor. Integration tests wrap in try-catch for environments without MSBuild.

## Conventions

- All public analysis classes are in `AiCodeGraph.Core` namespace subtrees
- Method IDs use the format from `SymbolIdGenerator.GetMethodId()` (fully qualified with parameter types)
- Tests follow pattern `{ClassName}Tests.cs` in the Tests project
- CLI commands return exit code 0 on success
- **Tree command**: Shows only public methods by default. Use `--include-private` to show all methods. Constructors (`.ctor`, `.cctor`) are always excluded.

## Auto-Context: Code Graph Integration

When modifying methods in this codebase, run the context command first if `./ai-code-graph/graph.db` exists:

```bash
ai-code-graph context "MethodName" --db ./ai-code-graph/graph.db
```

This returns complexity, callers, callees, cluster membership, and duplicates in one call. Use this information to:
- Avoid increasing complexity of already-complex methods (CC > 10)
- Update callers if you change a method's signature or behavior
- Apply the same fix to duplicates when fixing bugs
- Understand which intent cluster a method belongs to before refactoring

Available slash commands (primary):
- `/cg:analyze [solution]` - Analyze solution and build the graph
- `/cg:context <method>` - Full method context before editing (recommended first step)
- `/cg:query <pattern>` - Graph-based method retrieval (recommended for code lookup)
- `/cg:hotspots` - Top complexity hotspots
- `/cg:callgraph <method>` - Explore call relationships
- `/cg:impact <method>` - Transitive impact analysis

Available slash commands (secondary):
- `/cg:similar <method>` - Find methods with similar intent
- `/cg:token-search <query>` - Fallback: token-based search
- `/cg:semantic-search <query>` - Fallback: semantic search
- `/cg:duplicates` - Detected code clones
- `/cg:clusters` - Intent clusters
- `/cg:tree` - Code structure tree
- `/cg:export` - Export graph data
- `/cg:drift` - Architectural drift from baseline
- `/cg:churn` - Change-frequency x complexity hotspots
- `/cg:dead-code` - Find methods with no callers
- `/cg:coupling <method>` - Coupling metrics

To rebuild the graph after significant changes: `ai-code-graph analyze AiCodeGraph.sln`

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md
