# Task ID: 35

**Title:** Add Test Coverage Data to Context Command

**Status:** done

**Dependencies:** 20 ✓, 21 ✓, 22 ✓, 23 ✓, 24 ✓, 25 ✓, 26 ✓, 27 ✓, 28 ✓, 29 ✓

**Priority:** medium

**Description:** Show associated test methods in context output by querying the database for methods matching naming conventions (MethodNameTest, MethodNameTests).

**Details:**

File: AiCodeGraph.Cli/Program.cs context command

After existing context sections, query for test methods:

```csharp
// Test coverage section
var methodShortName = info.Name; // e.g., "BuildCallGraph"
var testMethods = await storage.SearchMethodsAsync($"%{methodShortName}%Test%", ct);
// Also search with Tests suffix
var testMethods2 = await storage.SearchMethodsAsync($"%{methodShortName}%Tests%", ct);

// Combine and deduplicate
var allTests = testMethods.Concat(testMethods2)
    .DistinctBy(t => t.Item1) // by method ID
    .Where(t => t.Item2.Contains("Test", StringComparison.OrdinalIgnoreCase)) // Filter to actual test classes
    .ToList();

if (allTests.Count > 0)
{
    Console.WriteLine($"\nTests: {string.Join(", ", allTests.Take(5).Select(t => t.Item2))} ({allTests.Count} found)");
}
else
{
    Console.WriteLine("\nTests: none found");
}
```

The SearchMethodsAsync already does LIKE pattern matching against method full names. We look for patterns like:
- `*Tests.*MethodName*` (test class convention)
- `*MethodName*Test` (test method convention)

Limit display to first 5 matches with count.

**Test Strategy:**

Test with a method that has known test coverage in the fixture (e.g., methods tested by existing test files). Verify output shows test method names and count. Test with a method that has no tests (shows 'none found'). Verify no false positives from non-test methods containing the word.

## Subtasks

### 35.1. Add test method discovery using SearchMethodsAsync with naming convention patterns

**Status:** pending  
**Dependencies:** None  

Query the database for test methods associated with the target method by using SearchMethodsAsync with LIKE patterns matching common test naming conventions (MethodName*Test*, *Tests*MethodName*) after existing context output sections.

**Details:**

In AiCodeGraph.Cli/Program.cs context command handler, after the existing context sections (metrics, callers, callees, cluster, duplicates), extract the method's short name from info.Name and perform two SearchMethodsAsync calls:
1. `$"%{methodShortName}%Test%"` to match test method naming conventions
2. `$"%{methodShortName}%Tests%"` to match test class naming conventions

Store both result sets for processing in the next step. The SearchMethodsAsync method already performs LIKE pattern matching against method full names in the SQLite database.

### 35.2. Deduplicate results, filter to test classes, limit display to 5, and add unit tests

**Status:** pending  
**Dependencies:** 35.1  

Combine the two search result sets, deduplicate by method ID, filter to entries that belong to actual test classes, limit display to first 5 matches with total count, and add test coverage for the feature.

**Details:**

After obtaining both search result sets from subtask 1:
1. Concatenate testMethods and testMethods2
2. Call `.DistinctBy(t => t.Item1)` to deduplicate by method ID
3. Filter with `.Where(t => t.Item2.Contains("Test", StringComparison.OrdinalIgnoreCase))` to ensure results are from actual test classes
4. Convert to list and output:
   - If count > 0: `Console.WriteLine($"\nTests: {string.Join(", ", allTests.Take(5).Select(t => t.Item2))} ({allTests.Count} found)");`
   - If count == 0: `Console.WriteLine("\nTests: none found");`
5. Add tests in AiCodeGraph.Tests verifying: correct output for methods with known tests, 'none found' for methods without tests, no false positives from non-test methods containing 'Test' substring, and proper deduplication when both patterns match the same method.
