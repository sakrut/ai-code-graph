# Task ID: 62

**Title:** Add Positional Solution Argument and --db Alias to Analyze Command

**Status:** done

**Dependencies:** None

**Priority:** medium

**Description:** Modify the analyze command to accept the solution path as an optional positional argument (common CLI pattern) and add --db as an alias for --output for better discoverability.

**Details:**

Update `AiCodeGraph.Cli/Program.cs` to support positional solution argument:

1. **Add positional argument** before the existing --solution option:
```csharp
// Add new positional argument (optional, nullable)
var solutionArgument = new Argument<string?>("solution")
{
    Description = "Path to .sln file (optional, auto-discovered if omitted)",
    Arity = ArgumentArity.ZeroOrOne
};

// Keep existing option for backwards compatibility
var solutionOption = new Option<string?>("--solution", "-s")
{
    Description = "Path to .sln file (alternative to positional argument)"
};

// Add --db as alias for --output
var outputOption = new Option<string>("--output", "-o")
{
    Description = "Output directory for the database",
    DefaultValueFactory = _ => "./ai-code-graph"
};
outputOption.AddAlias("--db"); // Add alias
```

2. **Update analyzeCommand construction**:
```csharp
var analyzeCommand = new Command("analyze", "Analyze a .NET solution and build the code graph")
{
    solutionArgument,  // Positional first
    solutionOption,    // --solution/-s option
    outputOption,      // --output/-o/--db option
    verboseOption,
    saveBaselineOption,
    embeddingEngineOption,
    embeddingModelOption,
    embeddingDimensionsOption
};
```

3. **Update action handler** to prefer positional over option:
```csharp
analyzeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    // Positional argument takes precedence, then --solution option, then auto-discover
    var solutionArg = parseResult.GetValue(solutionArgument);
    var solutionOpt = parseResult.GetValue(solutionOption);
    var solutionPath = solutionArg ?? solutionOpt;
    // ... rest uses solutionPath which may be null for auto-discovery
```

4. **Expected behavior after fix**:
```bash
# All of these should work:
ai-code-graph analyze backend/GuildsApi.sln              # positional
ai-code-graph analyze backend/GuildsApi.sln -o ./output  # positional + option
ai-code-graph analyze --solution backend/GuildsApi.sln   # explicit option
ai-code-graph analyze -s backend/GuildsApi.sln           # short option
ai-code-graph analyze                                     # auto-discover
ai-code-graph analyze MySolution.sln --db ./mydb         # --db alias
```

5. **Update help text** to show both usage patterns clearly.

**Test Strategy:**

1. Test positional argument: `ai-code-graph analyze test.sln` should work
2. Test --solution option still works: `ai-code-graph analyze --solution test.sln`
3. Test positional with other options: `ai-code-graph analyze test.sln -o ./output -v`
4. Test auto-discovery still works: `ai-code-graph analyze` in directory with single .sln
5. Test --db alias: `ai-code-graph analyze test.sln --db ./custom`
6. Test that --db and --output are equivalent
7. Test help output shows positional argument usage
8. Verify backwards compatibility: existing scripts using `-s` still work
