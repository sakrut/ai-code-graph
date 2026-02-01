# Task ID: 28

**Title:** Deduplicate Catch Blocks in Program.cs

**Status:** done

**Dependencies:** None

**Priority:** low

**Description:** Replace repeated identical catch blocks in the analyze command with a shared error handler method.

**Details:**

File: AiCodeGraph.Cli/Program.cs lines 197-220

Current pattern repeated for FileNotFoundException and InvalidOperationException:
```csharp
catch (FileNotFoundException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    return 1;
}
```

Replace with a shared helper:
```csharp
private static int HandleCommandError(Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    return 1;
}
```

Then catch blocks become:
```csharp
catch (FileNotFoundException ex) { return HandleCommandError(ex); }
catch (InvalidOperationException ex) { return HandleCommandError(ex); }
```

Or combine into a single catch if appropriate:
```csharp
catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
{
    return HandleCommandError(ex);
}
```

Apply this pattern to all commands that have identical catch blocks.

**Test Strategy:**

Verify error output format is unchanged - same color, same message format. Test with missing DB file to trigger FileNotFoundException. Test with invalid solution path to trigger InvalidOperationException. Compare output before and after refactor.

## Subtasks

### 28.1. Define HandleCommandError helper method in Program.cs

**Status:** pending  
**Dependencies:** None  

Create a static helper method HandleCommandError that encapsulates the repeated error-handling logic (writing colored error message to stderr, optionally printing stack trace, and setting exit code).

**Details:**

Add a private static method at the bottom of Program.cs (or in a suitable location among the existing helper methods like CountTypes/CountMethods):

```csharp
static void HandleCommandError(Exception ex, bool verbose, string prefix = "Error", int exitCode = 1)
{
    Console.Error.WriteLine($"{prefix}: {ex.Message}");
    if (verbose) Console.Error.WriteLine(ex.StackTrace);
    Environment.ExitCode = exitCode;
}
```

Also add an overload or handle the OperationCanceledException case (fixed message "Analysis cancelled.") and the general Exception case (prefix "Unexpected error", exitCode 2, full ex.ToString() in verbose mode). This centralizes all error formatting in one place.

### 28.2. Replace catch blocks in the analyze command with HandleCommandError

**Status:** pending  
**Dependencies:** 28.1  

Refactor the catch blocks at lines 197-219 in the analyze command to call the new HandleCommandError helper, reducing 4 catch blocks to concise one-liners.

**Details:**

Replace the existing catch blocks in the analyze command (lines 197-219) with:

```csharp
catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
{
    HandleCommandError(ex, verbose);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Analysis cancelled.");
    Environment.ExitCode = 1;
}
catch (Exception ex)
{
    HandleCommandError(ex, verbose, "Unexpected error", 2);
}
```

Alternatively, keep separate catch lines if the combined `when` pattern is less readable. The key point is the body of each catch uses the helper instead of duplicating Console color/write logic. Note the current code does NOT use Console.ForegroundColor (the task description template differs from actual code) - preserve the actual format: plain `Console.Error.WriteLine`.

### 28.3. Identify and refactor duplicate catch blocks in other commands

**Status:** pending  
**Dependencies:** 28.1, 28.2  

Scan Program.cs for other commands (callgraph, context, hotspots, similar, search, duplicates, clusters, tree, export, drift) that have identical catch block patterns and replace them with HandleCommandError calls.

**Details:**

Search the rest of Program.cs for catch blocks that follow the same pattern as the analyze command. For each command that has identical error-handling catch blocks:
1. Identify whether the command's action lambda has a `verbose` variable in scope
2. Replace the catch body with `HandleCommandError(ex, verbose)` (or `HandleCommandError(ex, false)` if no verbose option exists)
3. Use the same pattern for OperationCanceledException and general Exception as established in subtask 2

Ensure each command's specific error behavior (if any differs) is preserved. Commands that have unique error handling should not be changed.

### 28.4. Verify consistent error output format across all commands

**Status:** pending  
**Dependencies:** 28.2, 28.3  

Test that all refactored commands produce identical error output (message format, exit codes) as before the refactoring.

**Details:**

For each refactored command, verify:
1. FileNotFoundException produces: `Error: <message>` on stderr, exit code 1
2. InvalidOperationException produces: `Error: <message>` on stderr, exit code 1
3. OperationCanceledException produces: `Analysis cancelled.` (or command-appropriate message) on stderr, exit code 1
4. Unexpected exceptions produce: `Unexpected error: <message>` on stderr, exit code 2
5. Verbose mode prints stack trace for known exceptions and full ToString() for unexpected ones

Run the full test suite with `dotnet test` and confirm all 178+ tests pass without modification.

### 28.5. Clean up and ensure no dead code remains from the refactoring

**Status:** pending  
**Dependencies:** 28.3, 28.4  

Remove any leftover dead code, verify the helper method is used by all intended call sites, and confirm the final state of Program.cs is clean.

**Details:**

Final review pass on Program.cs:
1. Confirm HandleCommandError is called from all commands that previously had duplicated catch blocks
2. Remove any commented-out old catch block code
3. Ensure no unused `using` statements were introduced or left behind
4. Verify the helper method placement is consistent with existing code organization (near other helper methods like CountTypes, CountMethods)
5. Run `dotnet build` one final time to confirm clean compilation with no warnings
6. Run `dotnet test` to confirm all tests still pass
