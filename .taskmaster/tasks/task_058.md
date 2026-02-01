# Task ID: 58

**Title:** Add Compact Output Formatting Options to Tree Command

**Status:** done

**Dependencies:** 57 ✓

**Priority:** high

**Description:** Add --max-methods, --no-return-types, and --compact convenience flag to the tree command, implementing truncated method lists and a condensed markdown-style output format for LLM context initialization.

**Details:**

## Implementation

### 1. Add New CLI Options to Program.cs

```csharp
// Add after existing options (around line 294)
var maxMethodsOption = new Option<int?>("--max-methods") { Description = "Show first N methods per type, then '... (+X more)'" };
var noReturnTypesOption = new Option<bool>("--no-return-types") { Description = "Omit return type signatures" };
var compactOption = new Option<bool>("--compact") { Description = "Enable compact mode with sensible defaults" };

var treeCommand = new Command("tree", "Display code structure tree")
{
    nsFilterOption, typeFilterOption, treeFormatOption, treeDbOption, includePrivateOption,
    skipTestsOption, skipInterfacesOption, skipNsOption,
    maxMethodsOption, noReturnTypesOption, compactOption
};
```

### 2. Implement Compact Mode Defaults in Action Handler

```csharp
treeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    // Parse all options
    var nsFilter = parseResult.GetValue(nsFilterOption);
    var typeFilter = parseResult.GetValue(typeFilterOption);
    var format = parseResult.GetValue(treeFormatOption) ?? "tree";
    var dbPath = parseResult.GetValue(treeDbOption) ?? "./ai-code-graph/graph.db";
    var includePrivate = parseResult.GetValue(includePrivateOption);
    var skipTests = parseResult.GetValue(skipTestsOption);
    var skipInterfaces = parseResult.GetValue(skipInterfacesOption);
    var skipNs = parseResult.GetValue(skipNsOption);
    var maxMethods = parseResult.GetValue(maxMethodsOption);
    var noReturnTypes = parseResult.GetValue(noReturnTypesOption);
    var compact = parseResult.GetValue(compactOption);
    
    // Apply compact mode defaults (can be overridden by explicit flags)
    if (compact)
    {
        skipTests = skipTests || true;
        skipInterfaces = skipInterfaces || true;
        skipNs = skipNs ?? "Migrations,Models";
        maxMethods = maxMethods ?? 5;
        noReturnTypes = noReturnTypes || true;
    }
    // ...
```

### 3. Create Compact Tree Output Format (replace existing tree output logic around line 350-388)

```csharp
if (format == "json")
{
    // ... existing JSON handling, enhanced in Task 59 ...
}
else
{
    // Group data for output
    var grouped = rows
        .GroupBy(r => r.ProjectName)
        .OrderBy(g => g.Key);
    
    foreach (var project in grouped)
    {
        Console.WriteLine(compact ? $"# {project.Key}" : project.Key);
        
        var byNamespace = project
            .GroupBy(r => r.NamespaceName)
            .OrderBy(g => g.Key);
        
        foreach (var ns in byNamespace)
        {
            var nsDisplay = compact 
                ? ExtractLastNamespacePart(ns.Key)  // e.g., "Controllers" from "App.Controllers"
                : ns.Key;
            Console.WriteLine(compact ? $"\n## {nsDisplay}" : $"  {ns.Key}");
            
            var byType = ns
                .GroupBy(r => (r.TypeName, r.TypeKind))
                .OrderBy(g => g.Key.TypeName);
            
            foreach (var type in byType)
            {
                var methods = type.OrderBy(r => r.MethodName).ToList();
                
                if (compact)
                {
                    // Compact format: TypeName: Method1, Method2, Method3... (+N more)
                    var displayMethods = methods.Take(maxMethods ?? int.MaxValue).ToList();
                    var remaining = methods.Count - displayMethods.Count;
                    var methodList = string.Join(", ", displayMethods.Select(m => m.MethodName));
                    var suffix = remaining > 0 ? $"... (+{remaining} more)" : "";
                    Console.WriteLine($"  {type.Key.TypeName}: {methodList}{suffix}");
                }
                else
                {
                    // Existing verbose format
                    var kindTag = type.Key.TypeKind switch
                    {
                        "Class" => "[C]",
                        "Interface" => "[I]",
                        "Record" => "[R]",
                        "Struct" => "[S]",
                        "Enum" => "[E]",
                        _ => "[?]"
                    };
                    Console.WriteLine($"    {kindTag} {type.Key.TypeName}");
                    
                    var displayMethods = methods.Take(maxMethods ?? int.MaxValue).ToList();
                    foreach (var m in displayMethods)
                    {
                        var returnType = noReturnTypes ? "" : $"{m.ReturnType} ";
                        var visibilityTag = m.Accessibility != "Public" ? $" [{m.Accessibility.ToLower()}]" : "";
                        Console.WriteLine($"        {returnType}{m.MethodName}(){visibilityTag}");
                    }
                    
                    var remaining = methods.Count - displayMethods.Count;
                    if (remaining > 0)
                        Console.WriteLine($"        ... (+{remaining} more)");
                }
            }
        }
    }
}
```

### 4. Add Helper Method for Namespace Extraction

```csharp
static string ExtractLastNamespacePart(string fullNamespace)
{
    var lastDot = fullNamespace.LastIndexOf('.');
    return lastDot >= 0 ? fullNamespace[(lastDot + 1)..] : fullNamespace;
}
```

### 5. Update Setup-Claude Command Template

In the setup-claude command section (around line 1850), update the tree.md template:

```csharp
File.WriteAllText(treeCmd, $@"Display code structure tree.

Steps:
1. Run `ai-code-graph tree --compact --db {dbPath}` for LLM-friendly overview
2. Run `ai-code-graph tree --db {dbPath}` for full detailed view
3. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
4. Present the hierarchical structure: Projects > Namespaces > Types > Methods
");
```

**Test Strategy:**

1. **Unit test --max-methods**: Create type with 10 methods, run with --max-methods 3, verify output shows exactly 3 methods and '... (+7 more)' suffix.

2. **Unit test --no-return-types**: Run tree without flag, verify return types present. Run with --no-return-types, verify method names appear without return types.

3. **Unit test --compact format**: Run with --compact, verify markdown-style output:
   - Project names start with '# '
   - Namespace sections use '## '
   - Types show 'TypeName: method1, method2...'

4. **Compact mode defaults test**: Run with just --compact, verify:
   - Test projects excluded
   - Interfaces excluded  
   - Migrations/Models namespaces excluded
   - Max 5 methods per type
   - No return types in output

5. **Compact mode override test**: Run with --compact --max-methods 10, verify max-methods is 10 not 5.

6. **Output size test**: Run tree --compact on test fixture, verify output is significantly smaller than without --compact.
