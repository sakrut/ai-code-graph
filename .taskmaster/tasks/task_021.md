# Task ID: 21

**Title:** Static Readonly Stopwords in IntentClusterer

**Status:** done

**Dependencies:** None

**Priority:** medium

**Description:** Move the stopwords HashSet from inside GenerateLabel() to a private static readonly field to avoid repeated allocation on every call.

**Details:**

File: AiCodeGraph.Core/Duplicates/IntentClusterer.cs lines 161-166

Current code creates a new HashSet<string> with 42 stopwords every time GenerateLabel() is called. Move to class-level static field:

```csharp
public class IntentClusterer
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "set", "is", "has", "the", "a", "an", "to", "from", "of", "in",
        "on", "by", "for", "with", "and", "or", "not", "this", "that", "it",
        "void", "int", "string", "bool", "var", "new", "return", "null", "async", "await"
    };
    
    // ... existing fields and methods ...
    
    private static string GenerateLabel(List<string> memberIds, Dictionary<string, NormalizedMethod> methodMap)
    {
        // Remove local HashSet creation, use Stopwords field directly
        // Rest of logic remains the same
    }
}
```

Note: Use StringComparer.OrdinalIgnoreCase in the HashSet constructor for case-insensitive matching (preserving current behavior where token comparison is case-insensitive via ToLowerInvariant).

**Test Strategy:**

Existing IntentClusterer tests in DuplicateDetectionTests.cs must pass unchanged. Verify GenerateLabel produces identical output. Optionally add a benchmark test calling ClusterMethods 1000 times to show reduced allocations.

## Subtasks

### 21.1. Add static readonly Stopwords field to IntentClusterer class

**Status:** pending  
**Dependencies:** None  

Declare a private static readonly HashSet<string> field named 'Stopwords' at the class level in IntentClusterer.cs, initialized with StringComparer.OrdinalIgnoreCase and containing all 30 stopword entries currently in GenerateLabel().

**Details:**

Add the field declaration after line 8 (the _minPoints field) in IntentClusterer.cs. The field should be:

private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
{
    "get", "set", "is", "has", "the", "a", "an", "to", "from", "of", "in",
    "on", "by", "for", "with", "and", "or", "not", "this", "that", "it",
    "void", "int", "string", "bool", "var", "new", "return", "null", "async", "await"
};

Using StringComparer.OrdinalIgnoreCase preserves the current case-insensitive matching behavior.

### 21.2. Remove local stopWords variable from GenerateLabel method

**Status:** pending  
**Dependencies:** 21.1  

Delete the local HashSet<string> stopWords declaration on lines 161-166 of IntentClusterer.cs, since the data is now in the class-level static field.

**Details:**

Remove lines 161-166 which contain:
var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "get", "set", ...
};

This eliminates the per-call allocation that creates a new HashSet with 30 entries every time GenerateLabel is invoked.

### 21.3. Update GenerateLabel to reference the static Stopwords field

**Status:** pending  
**Dependencies:** 21.2  

Change the LINQ Where clause on line 176 from referencing the local 'stopWords' variable to referencing the class-level 'Stopwords' field.

**Details:**

In GenerateLabel(), update the token filtering line from:
.Where(t => !stopWords.Contains(t) && t.Length > 2)
to:
.Where(t => !Stopwords.Contains(t) && t.Length > 2)

Note the capitalized field name 'Stopwords' matching the static readonly field naming convention. The Contains() check uses the HashSet's OrdinalIgnoreCase comparer, so behavior is identical to before.

### 21.4. Run existing IntentClusterer tests to verify behavior preservation

**Status:** pending  
**Dependencies:** 21.3  

Execute all tests in IntentClustererTests class to confirm that the refactoring produces identical behavior, particularly the ClusterMethods_GeneratesLabels test.

**Details:**

Run: dotnet test --filter "IntentClustererTests"

All 6 existing tests must pass:
- ClusterMethods_SimilarMethods_GroupsTogether
- ClusterMethods_TooFewMethods_ReturnsEmpty
- ClusterMethods_AllDifferent_NoCluster
- ClusterMethods_CohesionInRange
- ClusterMethods_GeneratesLabels (most critical - verifies label output)
- ClusterMethods_ClusterIdsAreUnique

The ClusterMethods_GeneratesLabels test uses semantic payloads containing stopwords like 'check', 'user', 'permission' and verifies non-empty labels are produced, confirming the filtering still works correctly.

### 21.5. Run full test suite to confirm no regressions

**Status:** pending  
**Dependencies:** 21.4  

Run the complete test suite (dotnet test) to ensure the static field change doesn't cause any unexpected side effects across the project.

**Details:**

Run: dotnet test

All 178 tests across the project should pass. This confirms:
- No naming conflicts with the new Stopwords field
- No thread-safety issues from the static field (HashSet is only read, never mutated, so it's inherently thread-safe)
- No integration-level regressions in duplicate detection or clustering workflows
- The field doesn't interfere with any other IntentClusterer usage patterns in the codebase
