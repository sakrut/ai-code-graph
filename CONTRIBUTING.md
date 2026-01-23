# Contributing

## Getting Started

1. Clone the repository
2. Ensure .NET 8.0 SDK is installed
3. Run `dotnet build` to verify the project compiles
4. Run `dotnet test` to verify all tests pass

## Development Workflow

1. Create a feature branch from `main`
2. Implement your changes
3. Add tests for new functionality
4. Ensure all 178+ tests pass with `dotnet test`
5. Submit a pull request

## Code Style

- Follow standard C# conventions (PascalCase for public members, camelCase for locals)
- Use file-scoped namespaces
- Enable nullable reference types
- Keep methods focused and testable

## Adding a New CLI Command

1. Define the command in `AiCodeGraph.Cli/Program.cs` with options and handler
2. Implement core logic in the appropriate `AiCodeGraph.Core/` subdirectory
3. Add storage methods to `StorageService` if persistence is needed
4. Update `SchemaDefinition.cs` if new tables are required
5. Write tests in `AiCodeGraph.Tests/`

## Adding a New Analysis Module

1. Create a subdirectory under `AiCodeGraph.Core/`
2. Define models as records where possible
3. Keep the module independent - accept data, return results
4. Wire it into the analysis pipeline in `Program.cs` (analyze command)
5. Add storage support and tests

## Running Tests

```bash
dotnet test                                    # All tests
dotnet test --filter "DuplicateDetection"      # By class name
dotnet test --verbosity normal                 # Verbose output
```

Integration tests require MSBuild to be available and will skip gracefully if not present.

## Project Layout

See [README.md](README.md#project-structure) for the full project structure.
