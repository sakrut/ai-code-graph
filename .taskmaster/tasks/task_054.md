# Task ID: 54

**Title:** Extract Shared Test Helper Methods to TestHelpers Utility Class

**Status:** done

**Dependencies:** 25 ✓

**Priority:** medium

**Description:** Extract duplicate GetMethodBody, CreateWorkspace, CountMethodsInType, and temp-directory Dispose helpers from MetricsEngineTests, NormalizationTests, CallGraphBuilderTests, IntegrationTests, and other test files into a shared static TestHelpers utility class and a TempDirectoryFixture base class in AiCodeGraph.Tests to eliminate code duplication (hybrid score 1.000 clones).

**Details:**

## Implementation Plan

### Step 1: Create `TestHelpers.cs` Static Utility Class

Create new file: `AiCodeGraph.Tests/TestHelpers.cs`

```csharp
using AiCodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Tests;

/// <summary>
/// Shared test helper methods extracted from duplicate implementations
/// across MetricsEngineTests, NormalizationTests, and CallGraphBuilderTests.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Parses C# source and returns the body of the first method declaration.
    /// Returns BlockSyntax for block bodies, ArrowExpressionClauseSyntax for expression bodies.
    /// </summary>
    public static SyntaxNode? GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    /// <summary>
    /// Non-nullable variant for tests that always expect a body to be present.
    /// </summary>
    public static SyntaxNode GetMethodBodyRequired(string source)
    {
        return GetMethodBody(source) ?? throw new InvalidOperationException("Method has no body");
    }

    /// <summary>
    /// Creates a LoadedWorkspace from a single C# source string for testing.
    /// Includes a reference to System.Object's assembly.
    /// </summary>
    public static LoadedWorkspace CreateWorkspace(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var projectId = ProjectId.CreateNewId();
        var compilations = new Dictionary<ProjectId, Compilation> { { projectId, compilation } };
        return new LoadedWorkspace(null!, compilations, Array.Empty<WorkspaceDiagnosticInfo>());
    }

    /// <summary>
    /// Recursively counts all methods in a type, including nested types.
    /// </summary>
    public static int CountMethodsInType(AiCodeGraph.Core.Models.CodeGraph.TypeModel type)
    {
        return type.Methods.Count + type.NestedTypes.Sum(CountMethodsInType);
    }
}
```

### Step 2: Create `TempDirectoryFixture.cs` Base Class

Create new file: `AiCodeGraph.Tests/TempDirectoryFixture.cs`

```csharp
namespace AiCodeGraph.Tests;

/// <summary>
/// Base class for test fixtures that need a temp directory with cleanup.
/// Implements both IAsyncDisposable and IDisposable for flexibility.
/// </summary>
public abstract class TempDirectoryFixture : IAsyncDisposable, IDisposable
{
    protected readonly string TempDir;

    protected TempDirectoryFixture(string prefix)
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);
    }

    protected string GetDbPath(string filename = "graph.db")
        => Path.Combine(TempDir, filename);

    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public virtual void Dispose()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }
}
```

### Step 3: Refactor MetricsEngineTests.cs

- **CognitiveComplexityTests**: Replace private `GetMethodBody` (line 13) with calls to `TestHelpers.GetMethodBodyRequired()` (since it uses the non-nullable variant with `!`).
- **LinesOfCodeTests** (line ~190): Replace private `GetMethodBody` with `TestHelpers.GetMethodBody()`.
- **MaxNestingDepthTests** (line ~276): Replace private `GetMethodBody` with `TestHelpers.GetMethodBody()`.
- **MetricsEngineIntegrationTests** (line ~334): Replace private `CreateWorkspace` with `TestHelpers.CreateWorkspace()`.

### Step 4: Refactor NormalizationTests.cs

- **StructuralSignatureBuilderTests** (line ~54): Replace private `GetMethodBody` with `TestHelpers.GetMethodBody()`.
- **SemanticPayloadBuilderTests** (line ~123): Replace private `GetMethodBody` with `TestHelpers.GetMethodBody()`.

### Step 5: Refactor CallGraphBuilderTests.cs

- Replace private `CreateWorkspace` (line 12) with `TestHelpers.CreateWorkspace()`.

### Step 6: Refactor IntegrationTests.cs

- Replace private `CountMethodsInType` (line ~210) with `TestHelpers.CountMethodsInType()`.
- Inherit from `TempDirectoryFixture` instead of managing `_tempDir` manually:
  - Remove `_tempDir` field, constructor directory creation, and `DisposeAsync` cleanup.
  - Use `TempDir` property and `GetDbPath()` from base.
  - Keep `_fixturePath` logic in the constructor.

### Step 7: Refactor Other Temp-Directory Test Classes (Optional, recommended)

Consider migrating these classes to use `TempDirectoryFixture`:
- **CliCommandTests.cs**: Currently `IDisposable` with identical temp dir pattern (prefix "cli-test").
- **DriftDetectorTests.cs**: Currently `IDisposable` with temp dir pattern (prefix "drift-test").
- **SolutionDiscoveryTests.cs**: Currently `IDisposable` with temp dir pattern (prefix "acg-test").

Each migration:
1. Change class to extend `TempDirectoryFixture` with appropriate prefix.
2. Remove manual `_tempDir` field, constructor creation, and `Dispose()` cleanup.
3. Use `TempDir` and `GetDbPath()` from the base class.

### Step 8: Verify No Breaking Changes

- All 178+ tests must pass with `dotnet test`.
- No behavioral changes - only structural refactoring.

## Important Notes

- The `GetMethodBody` in CognitiveComplexityTests uses `!` (non-null assertion) while others use nullable return. Provide both `GetMethodBody` (nullable) and `GetMethodBodyRequired` (non-nullable with exception) to handle both patterns cleanly.
- `CreateWorkspace` implementations are byte-for-byte identical across MetricsEngineIntegrationTests and CallGraphBuilderTests.
- The `StorageServiceTests` and `SearchCommandTests` use in-memory SQLite (`:memory:`) without temp directories, so they don't benefit from `TempDirectoryFixture` - leave those as-is.
- Task 25 extracted `GetMethodBody` for the *production* code in `AiCodeGraph.Core/Shared/MethodBodyHelper.cs`. This task extracts the *test* helpers which have a different signature (accept `string source` rather than `BaseMethodDeclarationSyntax`).

**Test Strategy:**

1. **Compile verification**: Run `dotnet build AiCodeGraph.Tests` to confirm all refactored test files compile successfully with the shared helpers.

2. **Full test suite**: Run `dotnet test` and verify all 178+ tests pass with zero failures or skips that weren't previously skipped.

3. **TestHelpers unit tests**: Add `TestHelpersTests.cs` with:
   - `GetMethodBody_BlockBody_ReturnsBlockSyntax`: Parse a method with `{ }` body, assert returns `BlockSyntax`.
   - `GetMethodBody_ExpressionBody_ReturnsArrowExpression`: Parse `void M() => x;`, assert returns `ArrowExpressionClauseSyntax`.
   - `GetMethodBody_AbstractMethod_ReturnsNull`: Parse `abstract void M();`, assert returns null.
   - `GetMethodBodyRequired_WithBody_ReturnsNode`: Verify non-nullable variant works.
   - `GetMethodBodyRequired_NoBody_Throws`: Verify throws `InvalidOperationException` for abstract methods.
   - `CreateWorkspace_ProducesValidCompilation`: Verify returned workspace has one project with successful compilation (no diagnostics with Error severity).
   - `CreateWorkspace_ResolvesObjectType`: Verify `typeof(object)` reference resolves in the compilation.
   - `CountMethodsInType_FlatType_CountsMethods`: Create a TypeModel with 3 methods, verify returns 3.
   - `CountMethodsInType_NestedTypes_CountsRecursively`: Create a TypeModel with nested types containing methods, verify recursive count.

4. **TempDirectoryFixture tests**: Add `TempDirectoryFixtureTests.cs` with:
   - `Constructor_CreatesDirectory`: Instantiate concrete subclass, verify `TempDir` exists.
   - `GetDbPath_ReturnsCorrectPath`: Verify returns path within `TempDir`.
   - `Dispose_DeletesDirectory`: Dispose and verify directory is removed.
   - `DisposeAsync_DeletesDirectory`: Async dispose and verify cleanup.

5. **Duplication verification**: After refactoring, run the ai-code-graph duplicates command (or grep) to confirm `GetMethodBody` and `CreateWorkspace` no longer appear as private methods in multiple test classes.

6. **Regression check**: Compare test output before and after refactoring to ensure identical pass/fail behavior.
