# Task ID: 57

**Title:** Add Project and Type Filtering Options to Tree Command

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Extend the tree command with --skip-tests, --skip-interfaces, and --skip-ns options to filter out test projects, interface types, and specific namespace patterns from the tree output.

**Details:**

## Implementation

### 1. Add New CLI Options to Program.cs (around line 290-298)

```csharp
// Existing options
var nsFilterOption = new Option<string?>("--namespace", "-n") { Description = "Filter by namespace prefix" };
var typeFilterOption = new Option<string?>("--type") { Description = "Filter by type name" };
var treeFormatOption = new Option<string>("--format", "-f") { Description = "tree|json", DefaultValueFactory = _ => "tree" };
var treeDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };
var includePrivateOption = new Option<bool>("--include-private") { Description = "Include non-public methods" };

// NEW filtering options
var skipTestsOption = new Option<bool>("--skip-tests") { Description = "Exclude *.Tests projects" };
var skipInterfacesOption = new Option<bool>("--skip-interfaces") { Description = "Exclude interface types (I* prefix)" };
var skipNsOption = new Option<string?>("--skip-ns") { Description = "Exclude namespaces matching patterns (comma-separated)" };
```

### 2. Add Options to treeCommand (around line 296-298)

```csharp
var treeCommand = new Command("tree", "Display code structure tree")
{
    nsFilterOption, typeFilterOption, treeFormatOption, treeDbOption, includePrivateOption,
    skipTestsOption, skipInterfacesOption, skipNsOption
};
```

### 3. Parse New Options in Action Handler (around line 301-307)

```csharp
treeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var nsFilter = parseResult.GetValue(nsFilterOption);
    var typeFilter = parseResult.GetValue(typeFilterOption);
    var format = parseResult.GetValue(treeFormatOption) ?? "tree";
    var dbPath = parseResult.GetValue(treeDbOption) ?? "./ai-code-graph/graph.db";
    var includePrivate = parseResult.GetValue(includePrivateOption);
    // NEW
    var skipTests = parseResult.GetValue(skipTestsOption);
    var skipInterfaces = parseResult.GetValue(skipInterfacesOption);
    var skipNs = parseResult.GetValue(skipNsOption);
    // ...
```

### 4. Extend IStorageService.GetTreeAsync Signature

In `AiCodeGraph.Core/Storage/IStorageService.cs` (line 29):

```csharp
Task<List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)>> GetTreeAsync(
    string? namespaceFilter = null,
    string? typeFilter = null,
    bool includePrivate = false,
    bool includeConstructors = false,
    bool skipTests = false,
    bool skipInterfaces = false,
    string? excludeNamespaces = null,
    CancellationToken cancellationToken = default);
```

### 5. Implement Filtering in StorageService.GetTreeAsync

In `AiCodeGraph.Core/Storage/StorageService.cs` (around line 452-485):

```csharp
public async Task<List<(...)>> GetTreeAsync(
    string? namespaceFilter = null,
    string? typeFilter = null,
    bool includePrivate = false,
    bool includeConstructors = false,
    bool skipTests = false,
    bool skipInterfaces = false,
    string? excludeNamespaces = null,
    CancellationToken cancellationToken = default)
{
    EnsureConnection();
    using var cmd = _connection!.CreateCommand();
    var conditions = new List<string>();
    
    if (namespaceFilter != null)
        conditions.Add("n.FullName LIKE @ns");
    if (typeFilter != null)
        conditions.Add("t.Name LIKE @type");
    if (!includeConstructors)
        conditions.Add("m.Name NOT IN ('.ctor', '.cctor')");
    if (!includePrivate)
        conditions.Add("m.Accessibility = 'Public'");
    
    // NEW filtering conditions
    if (skipTests)
        conditions.Add("p.Name NOT LIKE '%.Tests'");
    if (skipInterfaces)
        conditions.Add("t.Kind != 'Interface'");
    if (!string.IsNullOrEmpty(excludeNamespaces))
    {
        var patterns = excludeNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var (pattern, idx) in patterns.Select((p, i) => (p, i)))
        {
            conditions.Add($"n.FullName NOT LIKE @exns{idx}");
        }
    }
    
    var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    cmd.CommandText = $"...{where}...";
    
    if (namespaceFilter != null)
        cmd.Parameters.AddWithValue("@ns", $"{namespaceFilter}%");
    if (typeFilter != null)
        cmd.Parameters.AddWithValue("@type", $"%{typeFilter}%");
    if (!string.IsNullOrEmpty(excludeNamespaces))
    {
        var patterns = excludeNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var (pattern, idx) in patterns.Select((p, i) => (p, i)))
        {
            cmd.Parameters.AddWithValue($"@exns{idx}", $"%{pattern}%");
        }
    }
    // ...
}
```

### 6. Update Caller in Program.cs

```csharp
var rows = await storage.GetTreeAsync(nsFilter, typeFilter, includePrivate, includeConstructors: false, skipTests, skipInterfaces, skipNs, cancellationToken);
```

**Test Strategy:**

1. **Unit test --skip-tests**: Create test database with projects 'MyApp' and 'MyApp.Tests'. Run tree with --skip-tests, verify only 'MyApp' appears in output.

2. **Unit test --skip-interfaces**: Create test database with class 'UserService' and interface 'IUserService'. Run tree with --skip-interfaces, verify only 'UserService' appears.

3. **Unit test --skip-ns**: Create test database with namespaces 'App.Controllers', 'App.Models', 'App.Migrations'. Run tree with --skip-ns 'Models,Migrations', verify only 'App.Controllers' namespace appears.

4. **Combination test**: Test all three filters together to verify AND logic works correctly.

5. **Backward compatibility test**: Run tree command without new flags, verify output matches pre-change behavior exactly.

6. **CLI help test**: Run `tree --help` and verify new options appear with descriptions.
