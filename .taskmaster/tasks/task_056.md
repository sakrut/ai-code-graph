# Task ID: 56

**Title:** Modify Tree Command to Filter Members by Visibility

**Status:** done

**Dependencies:** 8 ✓

**Priority:** medium

**Description:** Add visibility filtering to the tree command that shows only public members by default, with an optional --include-private flag to show private methods, and always excludes constructors regardless of visibility settings.

**Details:**

## Implementation Steps

### 1. Schema Change - Add Accessibility Column (AiCodeGraph.Core/Storage/SchemaDefinition.cs)

Add a new column to the Methods table definition:
```csharp
CREATE TABLE Methods (
    ...
    IsAbstract INTEGER NOT NULL DEFAULT 0,
    Accessibility TEXT NOT NULL DEFAULT 'Public'  // NEW COLUMN
);
```

### 2. Update InsertMethod to Persist Accessibility (AiCodeGraph.Core/Storage/StorageService.cs:141-162)

Modify the INSERT statement and add parameter:
```csharp
cmd.CommandText = """
    INSERT OR IGNORE INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, FilePath, IsStatic, IsAsync, IsVirtual, IsOverride, IsAbstract, Accessibility)
    VALUES (@id, @name, @fullName, @ret, @tid, @start, @end, @path, @isStatic, @isAsync, @isVirtual, @isOverride, @isAbstract, @accessibility)
    """;
// Add parameter for Accessibility:
cmd.Parameters.AddWithValue("@accessibility", method.Accessibility.ToString());
```

### 3. Update GetTreeAsync Signature and Query (AiCodeGraph.Core/Storage/StorageService.cs:451-490)

Change method signature to accept visibility filter:
```csharp
public async Task<List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)>> GetTreeAsync(
    string? namespaceFilter = null, 
    string? typeFilter = null,
    bool includePrivate = false,
    bool includeConstructors = false,  // Always false by default
    CancellationToken cancellationToken = default)
```

Update the SQL query:
```csharp
// Add constructor filter (always applied unless explicitly requested)
if (!includeConstructors)
    conditions.Add("m.Name NOT IN ('.ctor', '.cctor')");

// Add visibility filter (public-only by default)
if (!includePrivate)
    conditions.Add("m.Accessibility = 'Public'");

cmd.CommandText = $"""
    SELECT p.Name, n.FullName, t.Name, t.Kind, m.Name, m.ReturnType, m.Accessibility
    FROM Projects p
    JOIN Namespaces n ON n.ProjectId = p.Id
    JOIN Types t ON t.NamespaceId = n.Id
    JOIN Methods m ON m.TypeId = t.Id
    {where}
    ORDER BY p.Name, n.FullName, t.Name, m.Name
    """;
```

### 4. Update IStorageService Interface (AiCodeGraph.Core/Storage/IStorageService.cs:29)

Update the interface to match:
```csharp
Task<List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)>> GetTreeAsync(
    string? namespaceFilter = null, 
    string? typeFilter = null, 
    bool includePrivate = false,
    bool includeConstructors = false,
    CancellationToken cancellationToken = default);
```

### 5. Update Tree Command in CLI (AiCodeGraph.Cli/Program.cs:289-386)

Add new option:
```csharp
var includePrivateOption = new Option<bool>("--include-private") { Description = "Include non-public methods" };

var treeCommand = new Command("tree", "Display code structure tree")
{
    nsFilterOption, typeFilterOption, treeFormatOption, treeDbOption, includePrivateOption
};
```

Update the action to pass the new parameter:
```csharp
var includePrivate = parseResult.GetValue(includePrivateOption);
var rows = await storage.GetTreeAsync(nsFilter, typeFilter, includePrivate, false, cancellationToken);
```

Optionally, update the tree/JSON output to show visibility annotations for non-public methods when `--include-private` is used:
```csharp
// In tree format output:
var visibilityTag = row.Accessibility != "Public" ? $" [{row.Accessibility.ToLower()}]" : "";
Console.WriteLine($"        {row.ReturnType} {row.MethodName}(){visibilityTag}");
```

### 6. Update MCP Handler (AiCodeGraph.Cli/Mcp/Handlers/QueryHandler.cs)

Update the tree handler in McpServer to support the new parameter, exposing `includePrivate` as an optional tool parameter.

### 7. Important Notes

- The constructor names in Roslyn are `.ctor` (instance constructor) and `.cctor` (static constructor)
- `Accessibility` enum values from Roslyn: `Public`, `Internal`, `Protected`, `ProtectedOrInternal`, `ProtectedAndInternal`, `Private`, `NotApplicable`
- The `--include-private` flag includes ALL non-public methods (Internal, Protected, Private, etc.)
- Constructors are excluded regardless of the visibility filter to match the requirement

**Test Strategy:**

### Unit Tests (AiCodeGraph.Tests/QueryCommandsTests.cs)

1. **Test default visibility filter** - Verify `GetTreeAsync()` with no parameters excludes private methods:
   - Seed database with public method `CreateUser` and private method `ValidateUser`
   - Call `GetTreeAsync()` with defaults
   - Assert only public methods are returned

2. **Test includePrivate=true** - Verify private methods are included:
   - Seed database with mix of public/private methods
   - Call `GetTreeAsync(includePrivate: true)`
   - Assert both public and private methods are returned

3. **Test constructor exclusion** - Verify constructors are always excluded:
   - Add `.ctor` method to test fixture
   - Call `GetTreeAsync()` and `GetTreeAsync(includePrivate: true)`
   - Assert `.ctor` is not in results for either case

4. **Test combined filters** - Verify namespace/type filters work with visibility filter:
   - Call `GetTreeAsync(namespaceFilter: "MyApp", includePrivate: false)`
   - Verify correct filtering on both criteria

### Integration Tests (AiCodeGraph.Tests/CliCommandTests.cs)

1. **Test CLI default behavior** - Run `tree` command, verify only public methods appear
2. **Test CLI with --include-private** - Run `tree --include-private`, verify private methods appear
3. **Test JSON output with visibility** - Run `tree --format json --include-private`, verify accessibility field in JSON

### Manual Testing

1. Run `ai-code-graph analyze AiCodeGraph.sln` to rebuild database with new schema
2. Run `ai-code-graph tree` and verify:
   - No constructors shown
   - Only public methods shown
3. Run `ai-code-graph tree --include-private` and verify:
   - No constructors shown
   - Private/internal methods now visible
   - Visibility annotation appears for non-public methods
4. Run `ai-code-graph tree --format json --include-private` and verify JSON includes accessibility field
