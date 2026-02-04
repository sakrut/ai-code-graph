# Task ID: 33

**Title:** Add Source Code Snippet to Context Output

**Status:** done

**Dependencies:** 20 ✓, 21 ✓, 22 ✓, 23 ✓, 24 ✓, 25 ✓, 26 ✓, 27 ✓, 28 ✓, 29 ✓

**Priority:** medium

**Description:** Enhance the context command to show the first 20 lines of the target method's source code, reading from the file path stored in the database.

**Details:**

File: AiCodeGraph.Cli/Program.cs context command (lines 1034-1135)

After the existing context output sections, add a source snippet section:

```csharp
// After existing output (metrics, callers, callees, cluster, duplicates)

// Source snippet section
if (info.FilePath != null && File.Exists(info.FilePath))
{
    try
    {
        var lines = await File.ReadAllLinesAsync(info.FilePath, ct);
        var startIdx = Math.Max(0, info.StartLine - 1); // Convert 1-based to 0-based
        var endIdx = Math.Min(lines.Length, startIdx + 20);
        
        if (startIdx < lines.Length)
        {
            Console.WriteLine();
            Console.WriteLine("Source (first 20 lines):");
            for (int i = startIdx; i < endIdx; i++)
            {
                Console.WriteLine($"  {lines[i]}");
            }
        }
    }
    catch (IOException)
    {
        // Skip section silently if file can't be read
    }
}
```

Edge cases to handle:
- File not found (skip section, don't error)
- Method shorter than 20 lines (show all available lines)
- StartLine is 0 or out of range (skip section)
- File read permission error (catch IOException, skip)

**Test Strategy:**

Add test verifying context output includes source snippet for a known method in the test fixture. Test with method shorter than 20 lines. Test with non-existent file path (graceful skip). Test with StartLine = 0. Verify output format matches spec.

## Subtasks

### 33.1. Add source code snippet reading logic to context command output

**Status:** pending  
**Dependencies:** None  

Add file reading logic after existing context output sections (metrics, callers, callees, cluster, duplicates) in the context command in Program.cs. Read the source file using info.FilePath and info.StartLine, display first 20 lines of the method's source code with a 'Source (first 20 lines):' header.

**Details:**

In AiCodeGraph.Cli/Program.cs context command (around lines 1034-1135), after all existing output sections, add the source snippet section:

1. Check if info.FilePath is not null and File.Exists(info.FilePath)
2. Use File.ReadAllLinesAsync to read the file
3. Convert 1-based StartLine to 0-based index: startIdx = Math.Max(0, info.StartLine - 1)
4. Calculate endIdx = Math.Min(lines.Length, startIdx + 20)
5. Guard: if startIdx < lines.Length, print header 'Source (first 20 lines):' and output each line with two-space indent
6. Handle edge cases inline: skip if file not found (File.Exists check), skip if StartLine is 0 or out of range (startIdx >= lines.Length), show fewer lines if method is shorter than 20 lines (endIdx clamps to lines.Length)
7. Wrap in try-catch for IOException to silently skip on file read errors

### 33.2. Add tests for source snippet output including edge cases

**Status:** pending  
**Dependencies:** 33.1  

Add unit/integration tests verifying the source snippet section works correctly for normal methods, short methods, missing files, StartLine=0, and IOException scenarios. Use the test fixture's known methods with stored file paths in the database.

**Details:**

In AiCodeGraph.Tests, add tests (likely in ContextCommandTests.cs or a new SourceSnippetTests.cs):

1. Test normal case: Use a known fixture method with a valid FilePath and StartLine, verify output contains 'Source (first 20 lines):' header and the expected lines of source code with two-space indent
2. Test short method: Use a fixture method shorter than 20 lines, verify all available lines are shown without error
3. Test file not found: Mock or use a method record with a non-existent FilePath, verify the source section is simply omitted (no exception, no output)
4. Test StartLine = 0: Verify the section is skipped gracefully (startIdx would be -1, clamped to 0, but if StartLine is 0 meaning unknown, section should be skipped)
5. Test IOException: Simulate file read failure (e.g., locked file or permission issue), verify section is skipped silently
6. Verify output format matches spec: two-space indent per line, correct header text
