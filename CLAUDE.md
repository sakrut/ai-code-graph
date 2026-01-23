# Claude Code Instructions

## Project Overview

AI Code Graph is a .NET 8.0 Roslyn-based static analysis tool packaged as a global CLI tool. It analyzes .NET solutions to build semantic code graphs stored in SQLite.

## Build & Test

```bash
dotnet build                          # Build all projects
dotnet test                           # Run all tests (178 tests)
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

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md
