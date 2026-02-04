# Task ID: 59

**Title:** Apply Compact Filtering to JSON Output Format

**Status:** done

**Dependencies:** 57 ✓, 58 ✓

**Priority:** medium

**Description:** Extend the tree command's JSON output format to respect all filtering and compact options, including filter metadata in the response for transparency about what was excluded.

**Details:**

## Implementation

### 1. Update JSON Output Section in Program.cs (around line 327-348)

Replace the existing JSON output block with filtering-aware logic:

```csharp
if (format == "json")
{
    var grouped = rows
        .GroupBy(r => r.ProjectName)
        .Select(pg => new
        {
            name = pg.Key,
            namespaces = pg.GroupBy(r => r.NamespaceName).OrderBy(g => g.Key).Select(ng => new
            {
                name = ng.Key,
                types = ng.GroupBy(r => (r.TypeName, r.TypeKind)).OrderBy(g => g.Key.TypeName).Select(tg =>
                {
                    var allMethods = tg.OrderBy(r => r.MethodName).ToList();
                    var displayMethods = allMethods.Take(maxMethods ?? int.MaxValue).ToList();
                    var truncated = allMethods.Count - displayMethods.Count;
                    
                    return new
                    {
                        name = tg.Key.TypeName,
                        kind = tg.Key.TypeKind.ToLower(),
                        methods = displayMethods.Select(r => noReturnTypes
                            ? new { name = r.MethodName, accessibility = r.Accessibility.ToLower() }
                            : (object)new { name = r.MethodName, returnType = r.ReturnType, accessibility = r.Accessibility.ToLower() }),
                        truncatedCount = truncated > 0 ? truncated : (int?)null
                    };
                })
            })
        });
    
    // Build filter metadata object
    var filters = new
    {
        skipTests = skipTests,
        skipInterfaces = skipInterfaces,
        excludedNamespaces = string.IsNullOrEmpty(skipNs) 
            ? Array.Empty<string>() 
            : skipNs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        maxMethodsPerType = maxMethods,
        noReturnTypes = noReturnTypes
    };
    
    // Determine if any non-default filters are active
    var hasActiveFilters = skipTests || skipInterfaces || !string.IsNullOrEmpty(skipNs) || maxMethods.HasValue || noReturnTypes;
    
    var output = hasActiveFilters
        ? new { projects = grouped, compact = compact, filters = filters }
        : (object)new { projects = grouped };
    
    var json = System.Text.Json.JsonSerializer.Serialize(output,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
    Console.WriteLine(json);
}
```

### 2. Expected JSON Output Structure

**Without filters (backward compatible):**
```json
{
  "projects": [
    {
      "name": "MyApp",
      "namespaces": [
        {
          "name": "MyApp.Controllers",
          "types": [
            {
              "name": "UserController",
              "kind": "class",
              "methods": [
                { "name": "GetUser", "returnType": "Task<User>", "accessibility": "public" }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

**With --compact flag:**
```json
{
  "projects": [
    {
      "name": "MyApp",
      "namespaces": [
        {
          "name": "MyApp.Controllers",
          "types": [
            {
              "name": "UserController",
              "kind": "class",
              "methods": [
                { "name": "GetUser", "accessibility": "public" },
                { "name": "CreateUser", "accessibility": "public" },
                { "name": "UpdateUser", "accessibility": "public" },
                { "name": "DeleteUser", "accessibility": "public" },
                { "name": "ListUsers", "accessibility": "public" }
              ],
              "truncatedCount": 7
            }
          ]
        }
      ]
    }
  ],
  "compact": true,
  "filters": {
    "skipTests": true,
    "skipInterfaces": true,
    "excludedNamespaces": ["Migrations", "Models"],
    "maxMethodsPerType": 5,
    "noReturnTypes": true
  }
}
```

### 3. Update MCP Handler for Tree (if exists)

Check if there's an MCP handler for tree in `AiCodeGraph.Cli/Mcp/Handlers/` and update it to support the same filtering options with matching parameter names.

### 4. Handle Edge Cases

- When `noReturnTypes` is true but some consumer needs return types, they can explicitly pass `--no-return-types false` to override compact default
- `truncatedCount` field only appears when truncation occurred (null otherwise)
- Empty namespace arrays are still included for schema consistency
- Filter metadata only appears when at least one non-default filter is active

**Test Strategy:**

1. **JSON backward compatibility test**: Run `tree --format json` without any new options, verify output structure matches exactly the pre-change format (no 'filters' or 'compact' keys).

2. **JSON with --skip-tests**: Run with --skip-tests --format json, verify:
   - Test projects absent from projects array
   - `filters.skipTests` is `true` in output

3. **JSON with --max-methods**: Run with --max-methods 2 --format json on type with 5 methods, verify:
   - Only 2 methods in methods array
   - `truncatedCount: 3` present on type object
   - `filters.maxMethodsPerType: 2` in metadata

4. **JSON with --no-return-types**: Run with --no-return-types --format json, verify:
   - Method objects have `name` and `accessibility` but no `returnType` key
   - `filters.noReturnTypes: true` in metadata

5. **JSON with --compact**: Run with --compact --format json, verify:
   - `compact: true` in root
   - All default filters applied and documented in `filters` object
   - Output significantly smaller than without --compact

6. **jq compatibility test**: Run `tree --compact --format json | jq '.projects[].namespaces[].types[].name'` and verify it outputs clean type names without errors.

7. **Filter override test**: Run with `--compact --max-methods 10 --format json`, verify `filters.maxMethodsPerType` is 10 (override worked).
