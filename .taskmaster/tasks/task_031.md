# Task ID: 31

**Title:** Improve Duplicates Output with File Paths and Line Ranges

**Status:** done

**Dependencies:** 20 ✓, 21 ✓, 22 ✓, 23 ✓, 24 ✓, 25 ✓, 26 ✓, 27 ✓, 28 ✓, 29 ✓

**Priority:** medium

**Description:** Enhance the duplicates command output to show file path and line numbers for each method in clone pairs instead of just fully qualified names.

**Details:**

File: AiCodeGraph.Cli/Program.cs duplicates command (lines 589-650)

Current output format:
```
Type1  0.950  0.920  0.935  Namespace.Type.MethodA(params)
```

Target output format:
```
Type1  0.950  0.920  0.935  MethodA  src/File.cs:42-58
```

Implementation:
```csharp
// For each clone pair, fetch method info for both methods
var infoA = await storage.GetMethodInfoAsync(pair.MethodIdA, ct);
var infoB = await storage.GetMethodInfoAsync(pair.MethodIdB, ct);

// Extract short name and file location
var nameA = infoA?.Name ?? pair.MethodIdA;
var locA = infoA?.FilePath != null ? $"{infoA.Value.FilePath}:{infoA.Value.StartLine}" : "unknown";

var nameB = infoB?.Name ?? pair.MethodIdB;
var locB = infoB?.FilePath != null ? $"{infoB.Value.FilePath}:{infoB.Value.StartLine}" : "unknown";

// Format output
Console.WriteLine($"{pair.Type,-10} {pair.StructuralSimilarity:F3}  {pair.SemanticSimilarity:F3}  {pair.HybridScore:F3}  {nameA}  {locA}");
Console.WriteLine($"{"",-10} {"",-5}  {"",-5}  {"",-5}  {nameB}  {locB}");
```

Note: Need to compute end line. Either store LOC in Methods table lookup or estimate as StartLine + LOC from metrics. Use GetMethodMetricsAsync to get LOC and compute end line.

**Test Strategy:**

Add test in DuplicateDetectionTests that verifies output includes file paths and line numbers. Test with test fixture methods that have known file locations. Verify JSON output format also includes new fields. Test edge case where FilePath is null (graceful fallback).

## Subtasks

### 31.1. Add GetMethodInfoAsync and GetMethodMetricsAsync calls for clone pair methods

**Status:** pending  
**Dependencies:** None  

Fetch method info and metrics for both methods in each clone pair within the duplicates command output loop (lines 638-648 of Program.cs). Use GetMethodMetricsAsync to obtain LinesOfCode for computing end line as StartLine + LinesOfCode - 1.

**Details:**

In AiCodeGraph.Cli/Program.cs duplicates command table output section (lines 638-648), the code already calls GetMethodInfoAsync for each pair. Add GetMethodMetricsAsync calls for both methods to retrieve LinesOfCode. Compute endLine = StartLine + LinesOfCode - 1 for each method. Store results in local variables (metricsA, metricsB) alongside existing infoA, infoB. Handle nullable returns gracefully - if metrics are null, endLine is unavailable. Also update the JSON output section (lines 619-634) to include the new filePath and lineRange fields in the serialized object.

### 31.2. Format two-line output with short name and file:line-range location

**Status:** pending  
**Dependencies:** 31.1  

Replace the current FullName output with short method Name and a file location string formatted as 'FilePath:StartLine-EndLine', displaying each clone pair as two lines with the second line indented.

**Details:**

Modify the table output formatting in Program.cs to use infoA?.Name (short name) instead of infoA?.FullName, and append the location string. Build location as: if FilePath is not null and metrics exist, format as `{FilePath}:{StartLine}-{EndLine}`; if FilePath exists but no metrics, format as `{FilePath}:{StartLine}`; if FilePath is null, use 'unknown'. Update the header line to reflect new columns (e.g., 'Method', 'Location'). The output pattern per pair becomes:
  Line 1: Type(10) Hybrid(6) Struct(6) Seman(6)  NameA  LocationA
  Line 2: (indented spacing)                       NameB  LocationB
Also update the JSON output to include name, filePath, startLine, and endLine fields for both methodA and methodB.

### 31.3. Handle edge cases and update duplicate detection tests

**Status:** pending  
**Dependencies:** 31.1, 31.2  

Add graceful fallback handling for null FilePath, missing metrics data, and methods not found in the database. Update existing tests and add new test cases to verify the enhanced output format.

**Details:**

Edge cases to handle: (1) GetMethodInfoAsync returns null (method not in DB) - fall back to method ID string and 'unknown' location; (2) FilePath is null in method info - display 'unknown' for location; (3) GetMethodMetricsAsync returns null (no metrics stored) - display only StartLine without end line range; (4) Both info and metrics are null - display raw method ID and 'unknown'. Add tests in DuplicateDetectionTests.cs: test with complete data showing full format, test with null FilePath showing fallback, test with missing metrics showing StartLine-only format, test with completely missing method showing raw ID. Verify JSON output also handles these edge cases with null-safe serialization.
